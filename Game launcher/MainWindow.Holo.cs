using System;
using System.Collections.Generic;
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
        // ───────────────────────────── Thema 2: Holo-Seiten ─────────────────────────────
        //
        // Jeder Bereich der Leiste bekommt in Thema 2 eine eigene, von Grund auf neu gebaute Seite aus schwebenden
        // Glas-Panels (abgeschrägte Ecken, Neonkante, HUD-Ecken, große Zahlen, Ringanzeigen). Die Seite liegt als eigene
        // Ebene über dem alten Bereich aus Thema 1; der bleibt darunter geladen, damit alle Zeitgeber und Daten weiterlaufen.
        // Bereiche ohne Holo-Seite zeigen weiter die alte Seite im Cyber-Stil.

        private Grid? holoHost;
        private ScrollViewer? holoScroll;
        private Grid? holoContent;
        private string holoKey = string.Empty;
        private TextBlock? holoClock;
        private DateTime holoAnimatingUntil;

        private static readonly System.Windows.Media.Color HoloCyan = System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE);

        /// <summary>Welche alte Seite durch eine Holo-Seite ersetzt wird (null = noch keine).</summary>
        private string? HoloKeyFor(FrameworkElement view)
        {
            if (view == ViewDashboard) return "dashboard";
            if (view == ViewStats) return "stats";
            if (view == ViewApps) return "apps";
            if (view == ViewLinks) return "links";
            if (view == ViewWishlist) return "wishlist";
            if (view == ViewDownloads) return "downloads";
            if (view == ViewSystem) return "system";
            if (view == ViewStorage) return "storage";
            return null;
        }

        private void EnsureHoloHost()
        {
            if (holoHost != null || RootGrid == null) return;

            holoHost = new Grid { Background = ConsoleInk, ClipToBounds = true, Visibility = Visibility.Collapsed };
            System.Windows.Controls.Panel.SetZIndex(holoHost, 26);
            holoHost.Children.Add(BuildHoloBackdrop());

            holoContent = new Grid();
            holoScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Focusable = false,
                Content = holoContent
            };
            holoHost.Children.Add(holoScroll);

            RootGrid.Children.Add(holoHost);
            PlaceHoloHost();
        }

        /// <summary>Gleiche Spalte wie die Bühne von START; oben Platz für Fensterleiste und Kopfleiste.</summary>
        private void PlaceHoloHost()
        {
            if (holoHost == null || holoContent == null) return;
            Grid.SetColumn(holoHost, NavPosition == "right" ? 0 : 1);
            holoContent.Margin = new Thickness(44, ConsoleTopInset + 22, 26, 36);
        }

        /// <summary>Aus ShowView: Gibt es für den Bereich eine Holo-Seite, wird sie gezeigt, sonst die Ebene ausgeblendet.</summary>
        private void ShowHoloPage(FrameworkElement view)
        {
            if (!IsConsole) return;
            EnsureHoloHost();
            if (holoHost == null) return;

            string? key = HoloKeyFor(view);
            if (key == null)
            {
                HideHoloPage();
                return;
            }

            bool same = key == holoKey && holoHost.Visibility == Visibility.Visible;
            holoKey = key;
            holoHost.Visibility = Visibility.Visible;
            PlaceHoloHost();
            BuildHoloPage(!same);
            if (!same) holoScroll?.ScrollToVerticalOffset(0);
        }

        private void HideHoloPage()
        {
            if (holoHost == null) return;
            holoHost.Visibility = Visibility.Collapsed;
            holoKey = string.Empty;
            holoClock = null;
            StopHoloLive();
        }

        /// <summary>Daten haben sich geändert: offene Holo-Seite ohne Einflug-Animation neu aufbauen.</summary>
        private void RefreshHoloPage()
        {
            if (holoHost == null || holoHost.Visibility != Visibility.Visible || holoKey.Length == 0) return;
            // Während die Panels einfliegen nicht neu aufbauen (sonst bricht die Animation ab)
            if (DateTime.Now < holoAnimatingUntil)
            {
                RequestConsoleRefresh();
                return;
            }
            double offset = holoScroll?.VerticalOffset ?? 0;
            BuildHoloPage(false);
            holoScroll?.ScrollToVerticalOffset(offset);
        }

        private void BuildHoloPage(bool animate)
        {
            if (holoContent == null) return;
            holoContent.Children.Clear();
            holoClock = null;
            StopHoloLive();   // Live-Werte nur auf der Seite, die sie selbst wieder startet
            holoPanelIndex = 0;
            holoAnimate = animate && settings.PageAnimations && !settings.PerformanceMode;
            if (holoAnimate) holoAnimatingUntil = DateTime.Now.AddMilliseconds(1500);

            FrameworkElement? page = holoKey switch
            {
                "dashboard" => BuildHoloDashboard(),
                "stats" => BuildHoloStats(),
                "apps" => BuildHoloApps(),
                "links" => BuildHoloLinks(),
                "wishlist" => BuildHoloWishlist(),
                "downloads" => BuildHoloDownloads(),
                "system" => BuildHoloSystem(),
                "storage" => BuildHoloStorage(),
                _ => null
            };
            if (page != null) holoContent.Children.Add(page);
            if (StreamerOn) Dispatcher.BeginInvoke(new Action(() => { ApplyCensorCategories(true); ScrubSensitiveText(); }), System.Windows.Threading.DispatcherPriority.Background);
        }

        // ───────── Hintergrund ─────────

        private FrameworkElement BuildHoloBackdrop()
        {
            var accent = GetAccentColor();
            var back = new Grid { IsHitTestVisible = false };

            // Feines Punkteraster
            var dot = new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x16, 0x8F, 0xB8, 0xFF)), null, new EllipseGeometry(new System.Windows.Point(1, 1), 1, 1));
            var tile = new DrawingBrush(new DrawingGroup { Children = { new GeometryDrawing(System.Windows.Media.Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 26, 26))), dot } })
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 26, 26),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            back.Children.Add(new Border { Background = tile });

            Border Glow(System.Windows.Point center, System.Windows.Media.Color color, byte alpha, double radius)
            {
                var brush = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = radius, RadiusY = radius * 1.3 };
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B), 0));
                brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0, color.R, color.G, color.B), 1));
                return new Border { Background = brush };
            }
            back.Children.Add(Glow(new System.Windows.Point(0.92, 0.0), accent, 0x30, 0.55));
            back.Children.Add(Glow(new System.Windows.Point(0.0, 1.0), HoloCyan, 0x1C, 0.5));

            // Unten: Fluchtlinien wie ein Boden aus Licht, sehr dezent
            var floor = new System.Windows.Shapes.Path
            {
                Stretch = Stretch.Fill,
                Height = 180,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Opacity = 0.35,
                StrokeThickness = 1,
                Data = BuildFloorGeometry()
            };
            var floorFade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            floorFade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0));
            floorFade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x66, accent.R, accent.G, accent.B), 1));
            floor.Stroke = floorFade;
            back.Children.Add(floor);
            return back;
        }

        private static Geometry BuildFloorGeometry()
        {
            var group = new GeometryGroup();
            // Strahlen aus dem Fluchtpunkt oben in der Mitte
            for (int i = -10; i <= 10; i++)
                group.Children.Add(new LineGeometry(new System.Windows.Point(500, 0), new System.Windows.Point(500 + i * 110, 200)));
            // Querlinien, nach unten immer weiter auseinander
            for (int i = 1; i <= 6; i++)
            {
                double y = 200 * Math.Pow(i / 6.0, 1.8);
                group.Children.Add(new LineGeometry(new System.Windows.Point(-600, y), new System.Windows.Point(1600, y)));
            }
            return group;
        }

        // ───────── Bausteine ─────────

        private int holoPanelIndex;
        private bool holoAnimate;

        /// <summary>Umriss mit abgeschrägter Ecke oben rechts und unten links.</summary>
        private static Geometry ChamferGeometry(double width, double height, double cut)
        {
            cut = Math.Min(cut, Math.Min(width, height) / 3);
            double w = Math.Max(1, width - 0.5), h = Math.Max(1, height - 0.5);
            var figure = new PathFigure { StartPoint = new System.Windows.Point(0.5, 0.5), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new PolyLineSegment(new[]
            {
                new System.Windows.Point(w - cut, 0.5),
                new System.Windows.Point(w, cut),
                new System.Windows.Point(w, h),
                new System.Windows.Point(cut, h),
                new System.Windows.Point(0.5, h - cut)
            }, true));
            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            return geometry;
        }

        private System.Windows.Media.Brush HoloEdgeBrush(byte strength = 0xCC)
        {
            var accent = GetAccentColor();
            var edge = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 1) };
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(strength, accent.R, accent.G, accent.B), 0));
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x30, 0x6F, 0x86, 0xB0), 0.45));
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)(strength * 0.6), HoloCyan.R, HoloCyan.G, HoloCyan.B), 1));
            return edge;
        }

        /// <summary>Glas-Fläche in der Form des Panels (wächst mit).</summary>
        private Grid HoloPlate(FrameworkElement content, double cut, out System.Windows.Shapes.Path edge)
        {
            var glassFill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            glassFill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xD9, 0x0E, 0x16, 0x26), 0));
            glassFill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xEB, 0x07, 0x0B, 0x14), 1));

            var shape = new System.Windows.Shapes.Path { Fill = glassFill, Stroke = HoloEdgeBrush(), StrokeThickness = 1, IsHitTestVisible = false };
            edge = shape;

            var root = new Grid();
            root.Children.Add(shape);
            root.Children.Add(content);
            root.SizeChanged += (s, e) =>
            {
                shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, cut);
                content.Clip = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, cut);
            };

            // HUD-Ecken oben links und unten rechts (die nicht abgeschrägten Ecken)
            foreach (var (h, v) in new[] { (System.Windows.HorizontalAlignment.Left, System.Windows.VerticalAlignment.Top), (System.Windows.HorizontalAlignment.Right, System.Windows.VerticalAlignment.Bottom) })
            {
                foreach (var (width, height) in new[] { (22.0, 2.0), (2.0, 22.0) })
                {
                    var corner = new Border
                    {
                        Width = width,
                        Height = height,
                        HorizontalAlignment = h,
                        VerticalAlignment = v,
                        Margin = new Thickness(h == System.Windows.HorizontalAlignment.Left ? -3 : 0, v == System.Windows.VerticalAlignment.Top ? -3 : 0,
                                               h == System.Windows.HorizontalAlignment.Right ? -3 : 0, v == System.Windows.VerticalAlignment.Bottom ? -3 : 0),
                        IsHitTestVisible = false
                    };
                    corner.SetResourceReference(Border.BackgroundProperty, h == System.Windows.HorizontalAlignment.Left ? "AccentBrush" : "AccentBrush");
                    if (h == System.Windows.HorizontalAlignment.Right) corner.Background = new SolidColorBrush(HoloCyan);
                    root.Children.Add(corner);
                }
            }
            return root;
        }

        /// <summary>Schwebendes Glas-Panel mit Kopfzeile (Kennung, Titel, Linie, optional Pfeil zum Öffnen).</summary>
        private FrameworkElement HoloPanel(string title, UIElement body, Action? open = null, double cut = 18, string? code = null)
        {
            var stack = new StackPanel { Margin = new Thickness(22, 16, 22, 20) };

            var head = new Grid { Margin = new Thickness(0, 0, 0, 14), Background = System.Windows.Media.Brushes.Transparent };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var mark = new Border { Width = 4, Height = 14, Margin = new Thickness(0, 0, 10, 0), RenderTransform = new SkewTransform(-18, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            head.Children.Add(mark);

            var caption = new TextBlock { Text = title.ToUpper(Loc.Culture), FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            caption.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            Grid.SetColumn(caption, 1);
            head.Children.Add(caption);

            var rule = ConsoleRule();
            rule.Margin = new Thickness(14, 0, 14, 0);
            rule.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            Grid.SetColumn(rule, 2);
            head.Children.Add(rule);

            var tail = new TextBlock
            {
                Text = open != null ? "" : (code ?? (holoPanelIndex + 1).ToString("00", Loc.Culture)),
                FontSize = open != null ? 13 : 10.5,
                Foreground = open != null ? ConsoleCyan : ConsoleMuted,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            tail.SetResourceReference(TextBlock.FontFamilyProperty, open != null ? "IconFont" : "MonoFont");
            Grid.SetColumn(tail, 3);
            head.Children.Add(tail);

            stack.Children.Add(head);
            stack.Children.Add(body);

            var plate = HoloPlate(stack, cut, out var edge);
            plate.Margin = new Thickness(0, 0, 18, 18);

            if (open != null)
            {
                head.Cursor = System.Windows.Input.Cursors.Hand;
                head.ToolTip = Loc.T("Öffnen");
                head.MouseLeftButtonUp += (s, e) => open();
                head.MouseEnter += (s, e) => { caption.Foreground = ConsoleCyan; edge.Stroke = HoloEdgeBrush(0xFF); };
                head.MouseLeave += (s, e) => { caption.Foreground = System.Windows.Media.Brushes.White; edge.Stroke = HoloEdgeBrush(); };
            }

            HoloEnter(plate, edge, plate);
            return plate;
        }

        /// <summary>
        /// Einflug: weich von unten hochgleiten und einblenden (nacheinander, nach Lage auf der Seite gestaffelt),
        /// bei Panels läuft dazu eine Lichtlinie über die Oberkante und die Kante glüht kurz nach.
        /// </summary>
        private void HoloEnter(FrameworkElement element, System.Windows.Shapes.Path? edge = null, Grid? plate = null)
        {
            int index = holoPanelIndex++;
            if (!holoAnimate) return;

            var move = new TranslateTransform(0, 26);
            element.RenderTransform = move;
            element.Opacity = 0;

            var begin = TimeSpan.FromMilliseconds(40 + Math.Min(index, 12) * 45);
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(620)) { BeginTime = begin, EasingFunction = ease });

            if (plate == null || edge == null) return;

            // Lichtlinie: wächst an der Oberkante von links nach rechts und verblasst
            var sweep = new Border { Height = 2, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(0, 0, 18, 0), IsHitTestVisible = false, Opacity = 0 };
            var accent = GetAccentColor();
            var fill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xFF, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0.85));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 1));
            sweep.Background = fill;
            var grow = new ScaleTransform(0, 1);
            sweep.RenderTransform = grow;
            plate.Children.Add(sweep);

            var lineBegin = begin + TimeSpan.FromMilliseconds(120);
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = lineBegin, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
            var fade = new DoubleAnimationUsingKeyFrames { BeginTime = lineBegin };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(820))));
            fade.Completed += (s, e) => plate.Children.Remove(sweep);
            sweep.BeginAnimation(UIElement.OpacityProperty, fade);

            // Kante glüht nach dem Ankommen kurz auf
            var glow = new DoubleAnimation(1, 2.2, TimeSpan.FromMilliseconds(260)) { BeginTime = lineBegin + TimeSpan.FromMilliseconds(380), AutoReverse = true, EasingFunction = new SineEase() };
            edge.BeginAnimation(System.Windows.Shapes.Shape.StrokeThicknessProperty, glow);
        }

        /// <summary>Ringanzeige: Spur, Bogen in Farbe (wächst beim Einblenden), gestrichelter Außenring, Zahl in der Mitte.</summary>
        private FrameworkElement HoloRing(double fraction, string value, string caption, double size = 132, System.Windows.Media.Color? color = null, double thickness = 9)
        {
            fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
            var tint = color ?? GetAccentColor();
            var grid = new Grid { Width = size, Height = size };

            grid.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, HoloCyan.R, HoloCyan.G, HoloCyan.B)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 1, 3 },
                IsHitTestVisible = false
            });
            const double gap = 7;   // Abstand zwischen gestricheltem Außenring und Spur
            grid.Children.Add(new System.Windows.Shapes.Ellipse { Margin = new Thickness(gap), Stroke = MakeBrush("#1C2740"), StrokeThickness = thickness, IsHitTestVisible = false });

            var arc = new System.Windows.Shapes.Path
            {
                Stroke = new SolidColorBrush(tint),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Flat,
                StrokeEndLineCap = PenLineCap.Flat,
                IsHitTestVisible = false
            };
            if (!settings.PerformanceMode) arc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = tint, BlurRadius = 14, ShadowDepth = 0, Opacity = 0.75 };
            grid.Children.Add(arc);

            double radius = size / 2 - gap - thickness / 2;   // Mitte der Spur
            void Draw(double f) => arc.Data = ArcGeometry(size / 2, radius, f);

            if (holoAnimate && fraction > 0)
            {
                Draw(0);
                var animation = new DoubleAnimation(0, fraction, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(250 + holoPanelIndex * 55), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                var clock = animation.CreateClock();
                clock.CurrentTimeInvalidated += (s, e) =>
                {
                    if (clock.CurrentProgress.HasValue) Draw(animation.GetCurrentValue(0.0, fraction, clock));
                };
            }
            else Draw(fraction);

            var center = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var number = new TextBlock { Text = value, FontSize = size * 0.2, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            center.Children.Add(number);
            if (caption.Length > 0)
            {
                var label = new TextBlock { Text = caption.ToUpper(Loc.Culture), FontSize = Math.Max(9, size * 0.075), Foreground = ConsoleMuted, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                center.Children.Add(label);
            }
            grid.Children.Add(center);
            return grid;
        }

        /// <summary>Bogen von „from“ bis „to“ (Anteile eines Kreises, 0 = oben, im Uhrzeigersinn).</summary>
        private static Geometry ArcSlice(double center, double radius, double from, double to)
        {
            if (to - from <= 0.0005) return Geometry.Empty;
            if (to - from >= 0.9995) return new EllipseGeometry(new System.Windows.Point(center, center), radius, radius);
            System.Windows.Point At(double f)
            {
                double angle = f * 2 * Math.PI - Math.PI / 2;
                return new System.Windows.Point(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));
            }
            var figure = new PathFigure { StartPoint = At(from), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(At(to), new System.Windows.Size(radius, radius), 0, to - from > 0.5, SweepDirection.Clockwise, true));
            return new PathGeometry(new[] { figure });
        }

        private static Geometry ArcGeometry(double center, double radius, double fraction)
        {
            if (fraction <= 0.0005) return Geometry.Empty;
            if (fraction >= 0.9995) return new EllipseGeometry(new System.Windows.Point(center, center), radius, radius);

            double angle = fraction * 2 * Math.PI - Math.PI / 2;
            var start = new System.Windows.Point(center, center - radius);
            var end = new System.Windows.Point(center + radius * Math.Cos(angle), center + radius * Math.Sin(angle));
            var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
            figure.Segments.Add(new ArcSegment(end, new System.Windows.Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true));
            return new PathGeometry(new[] { figure });
        }

        /// <summary>Schräge Zahlen-Platte für die Kopfzeile: große Zahl, Beschriftung, Lichtkante links.</summary>
        private FrameworkElement HoloStat(string caption, string value, Action? click = null, int? countTo = null)
        {
            var accent = GetAccentColor();
            var plate = new Border
            {
                BorderThickness = new Thickness(3, 1, 1, 1),
                Background = MakeBrush("#CC0C1322"),
                BorderBrush = new LinearGradientBrush(System.Windows.Media.Color.FromArgb(0xEE, accent.R, accent.G, accent.B), System.Windows.Media.Color.FromArgb(0x44, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new SkewTransform(-14, 0)
            };

            var text = new StackPanel { Margin = new Thickness(24, 12, 18, 14) };
            var label = new TextBlock { Text = caption.ToUpper(Loc.Culture), FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = ConsoleCyan };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            text.Children.Add(label);
            var number = new TextBlock
            {
                Text = value,
                FontSize = 30,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            };
            number.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            text.Children.Add(number);
            if (countTo is int target && holoAnimate) CountUp(number, target);

            var cell = new Grid { Margin = new Thickness(6, 0, 14, 18) };
            cell.Children.Add(plate);
            cell.Children.Add(text);
            if (click != null)
            {
                cell.Cursor = System.Windows.Input.Cursors.Hand;
                cell.MouseEnter += (s, e) => plate.Background = MakeBrush("#E6142035");
                cell.MouseLeave += (s, e) => plate.Background = MakeBrush("#CC0C1322");
                cell.MouseLeftButtonUp += (s, e) => click();
            }
            HoloEnter(cell);
            return cell;
        }

        /// <summary>Hinweistext in einem Panel.</summary>
        private TextBlock HoloHint(string text) => new()
        {
            Text = Loc.T(text),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = ConsoleMuted,
            Margin = new Thickness(0, 0, 0, 4)
        };

        /// <summary>Kleine Beschriftung in Cyan (Monospace).</summary>
        private TextBlock HoloLabel(string text, double size = 10.5)
        {
            var label = new TextBlock { Text = text.ToUpper(Loc.Culture), FontSize = size, FontWeight = FontWeights.Bold, Foreground = ConsoleCyan };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            return label;
        }

        /// <summary>Cover mit abgeschrägter Ecke; Klick öffnet das Spiel, Doppelklick startet es.</summary>
        private FrameworkElement HoloCover(GameItem game, double width, double height)
        {
            var image = new Border { Width = width, Height = height, Background = MakeBrush("#141C2E") };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage bitmap)
                image.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            else
            {
                var letter = new TextBlock
                {
                    Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                    FontSize = width * 0.32,
                    FontWeight = FontWeights.Bold,
                    Foreground = ConsoleMuted,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                letter.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                image.Child = letter;
            }
            double cut = Math.Round(width * 0.12);
            image.Clip = ChamferGeometry(width, height, cut);

            var edge = new System.Windows.Shapes.Path { Data = ChamferGeometry(width, height, cut), Stroke = MakeBrush("#33A5B4D0"), StrokeThickness = 1, IsHitTestVisible = false };

            var scale = new ScaleTransform(1, 1);
            var host = new Grid
            {
                Width = width,
                Height = height,
                Margin = new Thickness(0, 0, 14, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = scale,
                ToolTip = game.Name
            };
            host.Children.Add(image);
            host.Children.Add(edge);
            if (game.UpdatePending)
            {
                var badge = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(cut * 0.6, 0, 0, 6), Padding = new Thickness(6, 1, 6, 1), ToolTip = Loc.T("Update verfügbar") };
                badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                badge.Child = new TextBlock { Text = "", FontSize = 11, Foreground = MakeBrush(InkFor(GetAccentColor())), FontFamily = (System.Windows.Media.FontFamily)FindResource("IconFont") };
                host.Children.Add(badge);
            }

            var accent = GetAccentColor();
            host.MouseEnter += (s, e) =>
            {
                edge.Stroke = HoloEdgeBrush(0xFF);
                edge.StrokeThickness = 2;
                if (!settings.HoverAnimations) return;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
            };
            host.MouseLeave += (s, e) =>
            {
                edge.Stroke = MakeBrush("#33A5B4D0");
                edge.StrokeThickness = 1;
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(180)));
            };
            host.MouseLeftButtonUp += (s, e) =>
            {
                if (e.ClickCount > 1) return;
                ShowConsoleDetail(game);
            };
            host.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2) { e.Handled = true; LaunchGame(game); }
            };
            return host;
        }

        /// <summary>Schräger Knopf im Holo-Stil (primär = gefüllt in der Akzentfarbe).</summary>
        private FrameworkElement HoloButton(string text, Action click, bool primary = false, string? glyph = null)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(18, 9, 18, 9), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var ink = primary ? MakeBrush(InkFor(GetAccentColor())) : System.Windows.Media.Brushes.White;
            if (glyph != null)
            {
                var icon = new TextBlock { Text = glyph, FontSize = 14, Foreground = ink, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                row.Children.Add(icon);
            }
            var label = new TextBlock { Text = Loc.T(text).ToUpper(Loc.Culture), FontSize = 13, FontWeight = FontWeights.Bold, Foreground = ink, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            row.Children.Add(label);

            var plate = new Border { BorderThickness = new Thickness(1), RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new SkewTransform(-14, 0) };
            if (primary)
            {
                plate.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                plate.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
                if (!settings.PerformanceMode) plate.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            }
            else
            {
                plate.Background = MakeBrush("#A60E1526");
                plate.BorderBrush = MakeBrush("#55" + "22D3EE");
            }

            var button = new Grid { Margin = new Thickness(0, 0, 12, 10), Cursor = System.Windows.Input.Cursors.Hand };
            button.Children.Add(plate);
            button.Children.Add(row);
            button.MouseEnter += (s, e) =>
            {
                if (primary) plate.Opacity = 0.88;
                else { plate.BorderBrush = new SolidColorBrush(HoloCyan); plate.Background = MakeBrush("#CC12203A"); }
            };
            button.MouseLeave += (s, e) =>
            {
                if (primary) plate.Opacity = 1;
                else { plate.BorderBrush = MakeBrush("#5522D3EE"); plate.Background = MakeBrush("#A60E1526"); }
            };
            button.MouseLeftButtonUp += (s, e) => click();
            return button;
        }

        /// <summary>Kleine 7-Tage-Balken (heute in der Akzentfarbe).</summary>
        private FrameworkElement HoloWeekBars(double height = 46)
        {
            var days = Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6)).ToList();
            var values = days.Select(d => settings.PlayLog.TryGetValue(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), out double s) ? s : 0).ToList();
            double max = Math.Max(values.Max(), 1800);

            var bars = new Grid();
            for (int i = 0; i < 7; i++)
            {
                bars.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bool today = i == 6;
                var column = new StackPanel { ToolTip = days[i].ToString("dddd", Loc.Culture) + ": " + (values[i] >= 60 ? FormatPlaytime((long)values[i]) : "—") };
                var slot = new Grid { Height = height };
                var bar = new Border
                {
                    Height = Math.Max(3, height * values[i] / max),
                    VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                    Margin = new Thickness(4, 0, 4, 0),
                    RenderTransformOrigin = new System.Windows.Point(0.5, 1),
                    RenderTransform = new SkewTransform(-12, 0)
                };
                if (today)
                {
                    bar.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    if (!settings.PerformanceMode) bar.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
                }
                else bar.Background = values[i] > 0 ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x99, HoloCyan.R, HoloCyan.G, HoloCyan.B)) : MakeBrush("#1F2A44");
                if (holoAnimate)
                {
                    var grow = new ScaleTransform(1, 0);
                    bar.LayoutTransform = null;
                    bar.RenderTransform = new TransformGroup { Children = { grow, new SkewTransform(-12, 0) } };
                    grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { BeginTime = TimeSpan.FromMilliseconds(320 + i * 50), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
                slot.Children.Add(bar);
                column.Children.Add(slot);

                string day = days[i].ToString("ddd", Loc.Culture).TrimEnd('.');
                var label = new TextBlock { Text = (day.Length > 2 ? day[..2] : day).ToUpper(Loc.Culture), FontSize = 10, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0), Foreground = today ? System.Windows.Media.Brushes.White : ConsoleMuted };
                label.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                column.Children.Add(label);
                Grid.SetColumn(column, i);
                bars.Children.Add(column);
            }
            return bars;
        }

        /// <summary>Zeile mit Launcher-Symbol, Name und Wert rechts; Klick öffnet das Spiel.</summary>
        private FrameworkElement HoloGameRow(GameItem game, string? value = null, string? valueColor = null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2), Background = System.Windows.Media.Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new System.Windows.Controls.Image { Source = GetLauncherIcon(game.Source), Width = 16, Height = 16, Margin = new Thickness(0, 6, 10, 6), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            row.Children.Add(icon);

            var name = new TextBlock { Text = game.Name, FontSize = 14, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            if (value != null)
            {
                var right = new TextBlock { Text = value, FontSize = 12, Foreground = valueColor != null ? MakeBrush(valueColor) : ConsoleMuted, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                right.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(right, 2);
                row.Children.Add(right);
            }

            row.MouseEnter += (s, e) => name.Foreground = ConsoleCyan;
            row.MouseLeave += (s, e) => name.Foreground = System.Windows.Media.Brushes.White;
            row.MouseLeftButtonUp += (s, e) => ShowConsoleDetail(game);
            return row;
        }

        /// <summary>Zwei Spalten (links breit, rechts schmal); bei schmalem Fenster untereinander.</summary>
        private static FrameworkElement HoloColumns(System.Windows.Controls.Panel left, System.Windows.Controls.Panel right, double rightWidth = 0.42)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - rightWidth, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(rightWidth, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(left);
            grid.Children.Add(right);

            void Arrange(double width)
            {
                bool narrow = width > 0 && width < 1050;
                Grid.SetColumn(right, narrow ? 0 : 1);
                Grid.SetRow(right, narrow ? 1 : 0);
                Grid.SetColumnSpan(left, narrow ? 2 : 1);
                Grid.SetColumnSpan(right, narrow ? 2 : 1);
            }
            Arrange(0);
            grid.SizeChanged += (s, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) Arrange(e.NewSize.Width); };
            return grid;
        }

        /// <summary>Kacheln füllen die ganze Breite: so viele Spalten wie passen, alle gleich breit.</summary>
        private static void HoloFitWrap(WrapPanel wrap, double minItemWidth)
        {
            wrap.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width <= 0) return;
                int columns = Math.Max(1, (int)(e.NewSize.Width / minItemWidth));
                double item = Math.Floor(e.NewSize.Width / columns);
                if (Math.Abs(wrap.ItemWidth - item) > 0.5) wrap.ItemWidth = item;
            };
        }

        /// <summary>Raster gleich breiter Karten: so viele Spalten, wie bei der Mindestbreite passen.</summary>
        private static System.Windows.Controls.Primitives.UniformGrid HoloCardGrid(double minWidth)
        {
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, VerticalAlignment = System.Windows.VerticalAlignment.Top };
            grid.SizeChanged += (s, e) =>
            {
                if (e.NewSize.Width <= 0) return;
                int columns = Math.Max(1, (int)(e.NewSize.Width / minWidth));
                if (grid.Columns != columns) grid.Columns = columns;
            };
            return grid;
        }

        /// <summary>Suchfeld im Holo-Stil: Lupe, Platzhalter, schräger Rahmen; meldet jede Änderung.</summary>
        private FrameworkElement HoloSearchBox(string placeholder, string initial, Action<string> changed, double width = 320)
        {
            var box = new System.Windows.Controls.TextBox
            {
                Text = initial,
                Height = 40,
                Width = width,
                Padding = new Thickness(38, 0, 12, 0),
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                FontSize = 14,
                Background = MakeBrush("#CC0B1222"),
                BorderBrush = MakeBrush("#5522D3EE"),
                Foreground = System.Windows.Media.Brushes.White,
                CaretBrush = new SolidColorBrush(HoloCyan)
            };
            var icon = new TextBlock { Text = "", FontSize = 14, Foreground = ConsoleCyan, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, IsHitTestVisible = false };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var hint = new TextBlock { Text = Loc.T(placeholder), FontSize = 14, Foreground = ConsoleMuted, Margin = new Thickness(40, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, IsHitTestVisible = false, Visibility = initial.Length == 0 ? Visibility.Visible : Visibility.Collapsed };
            box.TextChanged += (s, e) =>
            {
                hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                changed(box.Text.Trim());
            };
            box.GotKeyboardFocus += (s, e) => box.BorderBrush = new SolidColorBrush(HoloCyan);
            box.LostKeyboardFocus += (s, e) => box.BorderBrush = MakeBrush("#5522D3EE");

            var host = new Grid { Margin = new Thickness(0, 0, 12, 10), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            host.Children.Add(box);
            host.Children.Add(icon);
            host.Children.Add(hint);
            return host;
        }

        /// <summary>Eintrag einer senkrechten Schiene (Kategorien, Bereiche): schräge Platte, aktiv mit Lichtkante und Leuchten.</summary>
        private FrameworkElement HoloRailItem(string label, string? count, bool active, Action click, string? glyph = null)
        {
            var row = new Grid { Margin = new Thickness(16, 10, 14, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (glyph != null)
            {
                var icon = new TextBlock { Text = glyph, FontSize = 15, Width = 28, Foreground = active ? ConsoleCyan : ConsoleMuted, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, glyph.Length == 1 && glyph[0] >= '' && glyph[0] <= '' ? "IconFont" : "DisplayFont");
                row.Children.Add(icon);
            }
            var text = new TextBlock { Text = label.ToUpper(Loc.Culture), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = active ? System.Windows.Media.Brushes.White : MakeBrush("#B8C4D8"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            if (count != null)
            {
                var number = new TextBlock { Text = count, FontSize = 11, Foreground = active ? ConsoleCyan : ConsoleMuted, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                number.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(number, 2);
                row.Children.Add(number);
            }

            var accent = GetAccentColor();
            var plate = new Border
            {
                BorderThickness = new Thickness(active ? 3 : 0, 0, 0, 0),
                Background = active ? new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, accent.R, accent.G, accent.B)) : System.Windows.Media.Brushes.Transparent,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new SkewTransform(-12, 0)
            };
            plate.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (active && !settings.PerformanceMode) plate.SetResourceReference(UIElement.EffectProperty, "NeonGlow");

            var item = new Grid { Margin = new Thickness(0, 0, 0, 4), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
            item.Children.Add(plate);
            item.Children.Add(row);
            if (!active)
            {
                item.MouseEnter += (s, e) => { plate.Background = MakeBrush("#1A22D3EE"); text.Foreground = ConsoleCyan; };
                item.MouseLeave += (s, e) => { plate.Background = System.Windows.Media.Brushes.Transparent; text.Foreground = MakeBrush("#B8C4D8"); };
            }
            item.MouseLeftButtonUp += (s, e) => click();
            return item;
        }

        /// <summary>Seitenkopf: Kennung in Cyan, großer Titel mit Glitch, Untertitel, rechts Knöpfe.</summary>
        private FrameworkElement HoloHeader(string code, string title, string? subtitle, params FrameworkElement[] actions)
        {
            var head = new Grid { Margin = new Thickness(0, 0, 18, 22) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            var eyebrow = ConsoleEyebrow("// " + code.ToUpper(Loc.Culture));
            eyebrow.Margin = new Thickness(2, 0, 0, 2);
            left.Children.Add(eyebrow);
            var big = ConsoleTitle(title, 50);
            left.Children.Add(big);
            MakeGlitchText(big);   // Farbkopien dahinter, glitcht zusammen mit dem Logo
            if (!string.IsNullOrEmpty(subtitle))
            {
                var sub = new TextBlock { Text = subtitle, FontSize = 15, Foreground = ConsoleMuted, Margin = new Thickness(2, 4, 0, 0), TextWrapping = TextWrapping.Wrap };
                left.Children.Add(sub);
            }
            head.Children.Add(left);

            if (actions.Length > 0)
            {
                var right = new WrapPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(24, 0, -12, 0) };
                foreach (var action in actions) right.Children.Add(action);
                Grid.SetColumn(right, 1);
                head.Children.Add(right);
            }
            HoloEnter(head);
            return head;
        }
    }
}
