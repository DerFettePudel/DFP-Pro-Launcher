using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Systemüberwachung („Kern“) ─────────────────────────────
        //
        // Drei große Live-Ringe (CPU, RAM, GPU) mit Verlaufslinie, Netzwerk mit großen Pfeil-Zahlen, Ping als Leuchtbalken,
        // Laufwerke als Segmentleisten, Sensoren als kleine Kacheln, Hardware als Tabellen. Die Messwerte kommen aus der
        // Messung von Thema 1 (läuft weiter, weil die alte Seite darunter sichtbar bleibt); ein eigener Zeitgeber zeichnet nach.

        private readonly DispatcherTimer holoLiveTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
        private Action? holoLiveUpdate;
        private DateTime holoLiveSlow = DateTime.MinValue;
        private SensorReport? lastSensorReport;
        private bool holoLiveHooked;

        private void StartHoloLive(Action update)
        {
            holoLiveUpdate = update;
            if (!holoLiveHooked)
            {
                holoLiveHooked = true;
                holoLiveTimer.Tick += (s, e) =>
                {
                    if (holoLiveUpdate == null || holoHost?.Visibility != Visibility.Visible || !IsVisible || WindowState == WindowState.Minimized) return;
                    try { holoLiveUpdate(); } catch { }
                };
            }
            holoLiveSlow = DateTime.MinValue;
            holoLiveTimer.Interval = TimeSpan.FromMilliseconds(settings.PerformanceMode ? 500 : 60);
            holoLiveTimer.Start();
        }

        private void StopHoloLive()
        {
            holoLiveUpdate = null;
            holoLiveTimer.Stop();
        }

        /// <summary>Großer Live-Ring: gibt das Element und eine Funktion zum Nachzeichnen zurück.</summary>
        private (FrameworkElement Element, Action<double, string, string> Update) HoloLiveRing(string caption, System.Windows.Media.Color tint, double size = 196)
        {
            const double thickness = 14, gap = 10;
            var grid = new Grid { Width = size, Height = size };
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, HoloCyan.R, HoloCyan.G, HoloCyan.B)), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 1, 3 } });

            // Skala: 20 kleine Striche außen
            var ticks = new GeometryGroup();
            for (int i = 0; i < 20; i++)
            {
                double angle = i / 20.0 * 2 * Math.PI - Math.PI / 2;
                double r1 = size / 2 - 3, r2 = size / 2 - (i % 5 == 0 ? 9 : 6);
                ticks.Children.Add(new LineGeometry(new System.Windows.Point(size / 2 + r1 * Math.Cos(angle), size / 2 + r1 * Math.Sin(angle)), new System.Windows.Point(size / 2 + r2 * Math.Cos(angle), size / 2 + r2 * Math.Sin(angle))));
            }
            grid.Children.Add(new System.Windows.Shapes.Path { Data = ticks, Stroke = MakeBrush("#3A4766"), StrokeThickness = 1.5 });

            grid.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(gap), Stroke = MakeBrush("#18223A"), StrokeThickness = thickness });
            var arc = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(tint), StrokeThickness = thickness };
            if (!settings.PerformanceMode) arc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = tint, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 };
            grid.Children.Add(arc);

            var center = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var label = HoloLabel(caption, 11);
            label.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            center.Children.Add(label);
            var number = ConsoleTitle("0%", 40);
            number.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            number.Effect = null;
            center.Children.Add(number);
            var sub = new TextBlock { FontSize = 11.5, Foreground = ConsoleMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            sub.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            center.Children.Add(sub);
            grid.Children.Add(center);

            double radius = size / 2 - gap - thickness / 2;
            void Update(double fraction, string value, string subtitle)
            {
                arc.Data = ArcGeometry(size / 2, radius, Math.Clamp(fraction, 0, 1));
                if (number.Text != value) number.Text = value;
                if (sub.Text != subtitle) sub.Text = subtitle;
            }
            return (grid, Update);
        }

        /// <summary>Verlaufslinie (letzte Messwerte, 0–100) als Fläche mit Lichtkante.</summary>
        private (FrameworkElement Element, Action<List<double>> Update) HoloSparkline(System.Windows.Media.Color tint, double height = 46)
        {
            var host = new Grid { Height = height, ClipToBounds = true, Margin = new Thickness(0, 14, 0, 0) };
            host.Children.Add(new Border { BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = MakeBrush("#2A3756") });
            var fill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x66, tint.R, tint.G, tint.B), 0));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1));
            var area = new System.Windows.Shapes.Path { Fill = fill };
            var line = new System.Windows.Shapes.Path { Stroke = new SolidColorBrush(tint), StrokeThickness = 1.6 };
            host.Children.Add(area);
            host.Children.Add(line);

            void Update(List<double> values)
            {
                double width = host.ActualWidth;
                if (width <= 0 || values.Count < 2) return;
                int count = Math.Max(values.Count, 30);
                double step = width / (count - 1);
                double startX = width - step * (values.Count - 1);
                var points = values.Select((v, i) => new System.Windows.Point(startX + i * step, height - 2 - Math.Clamp(v, 0, 100) / 100.0 * (height - 6))).ToList();

                var lineFigure = new PathFigure { StartPoint = points[0] };
                lineFigure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
                line.Data = new PathGeometry(new[] { lineFigure });

                var areaFigure = new PathFigure { StartPoint = new System.Windows.Point(points[0].X, height), IsClosed = true };
                areaFigure.Segments.Add(new PolyLineSegment(points.Append(new System.Windows.Point(points[^1].X, height)), true));
                area.Data = new PathGeometry(new[] { areaFigure });
            }
            return (host, Update);
        }

        private FrameworkElement BuildHoloSystem()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Systemüberwachung"), Loc.T("Systemüberwachung"), Loc.T("Live-Auslastung deines PCs, aktualisiert jede Sekunde."),
                HoloButton("Ping testen", () => BtnPing_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Neu auslesen", () => BtnRefreshHardware_Click(this, new RoutedEventArgs()), false, "")));

            // ── Kern: drei Ringe ──
            var accent = GetAccentColor();
            var cpuColor = accent;
            var ramColor = MakeColor("#A78BFA");
            var gpuColor = HoloCyan;

            var core = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
            var cpu = HoloLiveRing("CPU", cpuColor);
            var ram = HoloLiveRing("RAM", ramColor);
            var gpu = HoloLiveRing("GPU", gpuColor);
            var cpuSpark = HoloSparkline(cpuColor);
            var ramSpark = HoloSparkline(ramColor);
            var gpuSpark = HoloSparkline(gpuColor);
            var cpuInfo = new TextBlock { FontSize = 12, Foreground = MakeBrush("#C9D3E3"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 10, 0, 0) };
            var ramInfo = new TextBlock { FontSize = 12, Foreground = MakeBrush("#C9D3E3"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 10, 0, 0) };
            var gpuInfo = new TextBlock { FontSize = 12, Foreground = MakeBrush("#C9D3E3"), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 10, 0, 0) };

            StackPanel Column(string title, string? name, (FrameworkElement Element, Action<double, string, string> Update) ring, FrameworkElement spark, TextBlock info)
            {
                var column = new StackPanel { Margin = new Thickness(10, 0, 10, 0) };
                column.Children.Add(HoloLabel(title));
                column.Children.Add(new TextBlock { Text = name ?? string.Empty, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 14), ToolTip = name });
                ring.Element.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
                column.Children.Add(ring.Element);
                column.Children.Add(info);
                column.Children.Add(spark);
                return column;
            }
            core.Children.Add(Column(Loc.T("Prozessor"), TxtCpuName?.Text, cpu, cpuSpark.Element, cpuInfo));
            core.Children.Add(Column(Loc.T("Arbeitsspeicher"), TxtRamName?.Text, ram, ramSpark.Element, ramInfo));
            core.Children.Add(Column(Loc.T("Grafikkarte"), TxtGpuName?.Text, gpu, gpuSpark.Element, gpuInfo));
            page.Children.Add(HoloPanel(Loc.T("Kern"), core, null, 28));

            // ── Netzwerk, Ping ──
            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right, 0.5));

            var net = new Grid();
            net.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            net.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            StackPanel Speed(string caption, string color, out TextBlock value)
            {
                var box = new StackPanel();
                var label = HoloLabel(caption);
                label.Foreground = MakeBrush(color);
                box.Children.Add(label);
                value = ConsoleTitle("–", 34);
                box.Children.Add(value);
                return box;
            }
            var downBox = Speed("↓ " + Loc.T("Download"), "#34D399", out var down);
            var upBox = Speed("↑ " + Loc.T("Upload"), "#60A5FA", out var up);
            Grid.SetColumn(upBox, 1);
            net.Children.Add(downBox);
            net.Children.Add(upBox);
            var netBody = new StackPanel();
            netBody.Children.Add(net);
            var adapter = new TextBlock { FontSize = 12, Foreground = ConsoleMuted, Margin = new Thickness(0, 10, 0, 0) };
            adapter.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            netBody.Children.Add(adapter);
            left.Children.Add(HoloPanel(Loc.T("Netzwerk"), netBody));

            var drives = new StackPanel();
            left.Children.Add(HoloPanel(Loc.T("Laufwerke"), drives));

            right.Children.Add(HoloPanel(Loc.T("Netzwerk und Ping"), BuildHoloPing()));

            var sensors = new StackPanel();
            if (settings.SensorsEnabled) right.Children.Add(HoloPanel(Loc.T("Temperaturen und Lüfter"), sensors));

            // ── Hardware ──
            page.Children.Add(HoloPanel(Loc.T("Dein System"), BuildHoloHardware()));

            void FillDrives()
            {
                drives.Children.Clear();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
                        double totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                        double usedGb = totalGb - drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                        double percent = totalGb > 0 ? usedGb / totalGb : 0;
                        string label = drive.VolumeLabel.Length > 0 ? $"{drive.Name.TrimEnd('\\')}  {drive.VolumeLabel}" : drive.Name.TrimEnd('\\');
                        drives.Children.Add(HoloSegmentBar(label, percent, Loc.T($"{usedGb:F0} GB von {totalGb:F0} GB belegt ({percent * 100:F0} %)")));
                    }
                    catch { }
                }
            }

            void FillSensors()
            {
                if (!settings.SensorsEnabled) return;
                sensors.Children.Clear();
                var rows = lastSensorReport?.Rows;
                if (rows == null || rows.Count == 0)
                {
                    sensors.Children.Add(HoloHint(TxtSensorStatus?.Text is { Length: > 0 } status ? status : "Sensoren werden gelesen ..."));
                    return;
                }
                var wrap = new WrapPanel();
                foreach (var row in rows.Where(r => r.Kind is "temp" or "fan").Take(24)) wrap.Children.Add(HoloSensorTile(row));
                sensors.Children.Add(wrap);
            }

            StartHoloLive(() =>
            {
                // weich: Werte aus der Glättung von Thema 1
                cpu.Update(ringShown[0] / 100.0, $"{ringShown[0]:F0}%", TxtCpuSub?.Text ?? string.Empty);
                ram.Update(ringShown[1] / 100.0, $"{ringShown[1]:F0}%", TxtRamSub?.Text ?? string.Empty);
                bool hasGpu = lastSample?.HasGpu ?? false;
                gpu.Update(hasGpu ? ringShown[2] / 100.0 : 0, hasGpu ? $"{ringShown[2]:F0}%" : "–", TxtGpuSub?.Text ?? string.Empty);

                if ((DateTime.Now - holoLiveSlow).TotalMilliseconds < 1000) return;
                bool first = holoLiveSlow == DateTime.MinValue;
                holoLiveSlow = DateTime.Now;

                cpuSpark.Update(cpuHistory);
                ramSpark.Update(ramHistory);
                gpuSpark.Update(gpuHistory);
                cpuInfo.Text = TxtCpuDetail?.Text ?? string.Empty;
                ramInfo.Text = TxtRamDetail?.Text ?? string.Empty;
                gpuInfo.Text = TxtGpuDetail?.Text ?? string.Empty;
                down.Text = (TxtNetDown?.Text ?? "–").TrimStart('↓', ' ');
                up.Text = (TxtNetUp?.Text ?? "–").TrimStart('↑', ' ');
                adapter.Text = TxtNetName?.Text ?? string.Empty;
                FillSensors();
                if (first || DateTime.Now.Second % 5 == 0) FillDrives();
            });
            return page;
        }

        /// <summary>Leiste aus 24 schrägen Segmenten (belegt = leuchtend).</summary>
        private FrameworkElement HoloSegmentBar(string label, double fraction, string detail)
        {
            var box = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White });
            var percent = new TextBlock { Text = Math.Round(fraction * 100).ToString(Loc.Culture) + " %", FontSize = 12, Foreground = ConsoleCyan };
            percent.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(percent, 1);
            head.Children.Add(percent);
            box.Children.Add(head);

            const int segments = 24;
            int lit = (int)Math.Round(fraction * segments);
            string color = fraction > 0.9 ? "#EF4444" : fraction > 0.75 ? "#F59E0B" : string.Empty;
            var bar = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = segments, Height = 12, Margin = new Thickness(0, 7, 0, 5) };
            for (int i = 0; i < segments; i++)
            {
                var cell = new Border { Margin = new Thickness(1.5, 0, 1.5, 0), RenderTransform = new SkewTransform(-20, 0) };
                if (i < lit)
                {
                    if (color.Length > 0) cell.Background = MakeBrush(color);
                    else cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                }
                else cell.Background = MakeBrush("#18223A");
                bar.Children.Add(cell);
            }
            box.Children.Add(bar);
            var text = new TextBlock { Text = detail, FontSize = 11.5, Foreground = ConsoleMuted };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            box.Children.Add(text);
            return box;
        }

        private FrameworkElement HoloSensorTile(SensorRow row)
        {
            string color = row.Hot ? "#EF4444" : row.Warm ? "#F59E0B" : row.Kind == "fan" ? "#22D3EE" : "#34D399";
            var tile = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 10, 10), ToolTip = row.Group + " · " + row.Name };
            var plate = new Grid { Height = 68 };
            plate.Children.Add(new System.Windows.Shapes.Path { Data = ChamferGeometry(150, 68, 10), Fill = MakeBrush("#B30D1424"), Stroke = MakeBrush("#33" + color.TrimStart('#')), StrokeThickness = 1 });
            var texts = new StackPanel { Margin = new Thickness(12, 8, 10, 8) };
            var name = new TextBlock { Text = row.Name, FontSize = 11, Foreground = ConsoleMuted, TextTrimming = TextTrimming.CharacterEllipsis };
            texts.Children.Add(name);
            var value = new TextBlock { Text = row.Value, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = MakeBrush(color) };
            value.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            texts.Children.Add(value);
            plate.Children.Add(texts);
            tile.Children.Add(plate);
            return tile;
        }

        private FrameworkElement BuildHoloPing()
        {
            var body = new StackPanel();
            body.Children.Add(HoloHint(TxtPingInfo?.Text ?? string.Empty));
            if (PingList?.ItemsSource is IEnumerable<PingResult> results)
            {
                foreach (var result in results)
                {
                    var row = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                    var head = new Grid();
                    head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    head.Children.Add(new TextBlock { Text = result.Name, FontSize = 13.5, Foreground = System.Windows.Media.Brushes.White });
                    var value = new TextBlock { Text = result.Text, FontSize = 12, Foreground = result.BarBrush };
                    value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                    Grid.SetColumn(value, 1);
                    head.Children.Add(value);
                    row.Children.Add(head);
                    var track = new Grid { Height = 5, Margin = new Thickness(0, 6, 0, 0) };
                    track.Children.Add(new Border { Background = MakeBrush("#18223A"), RenderTransform = new SkewTransform(-20, 0) });
                    var bar = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = result.BarBrush, RenderTransform = new SkewTransform(-20, 0) };
                    double share = Math.Clamp(result.Percent / 100.0, 0.02, 1);
                    track.SizeChanged += (s, e) => bar.Width = e.NewSize.Width * share;
                    track.Children.Add(bar);
                    row.Children.Add(track);
                    body.Children.Add(row);
                }
            }
            return body;
        }

        private FrameworkElement BuildHoloHardware()
        {
            if (HardwareList?.ItemsSource is not IEnumerable<HardwareGroup> groups || !groups.Any())
                return HoloHint(TxtHardwareStatus?.Text is { Length: > 0 } status ? status : "Wird gelesen ...");

            var grid = HoloCardGrid(380);
            foreach (var group in groups)
            {
                var box = new StackPanel { Margin = new Thickness(0, 0, 24, 18) };
                var title = HoloLabel(group.Title, 11.5);
                title.Margin = new Thickness(0, 0, 0, 8);
                box.Children.Add(title);
                foreach (var row in group.Rows)
                {
                    var line = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    line.Children.Add(new TextBlock { Text = row.Label, FontSize = 12.5, Foreground = ConsoleMuted, TextTrimming = TextTrimming.CharacterEllipsis });
                    var value = new TextBlock { Text = row.Value, FontSize = 12.5, Foreground = System.Windows.Media.Brushes.White, TextWrapping = TextWrapping.Wrap };
                    Grid.SetColumn(value, 1);
                    line.Children.Add(value);
                    box.Children.Add(line);
                }
                grid.Children.Add(box);
            }
            return grid;
        }
    }
}
