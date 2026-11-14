using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Cyber-Feinschliff ─────────────────────────────
        //
        // Pudel-Logo als Neon-Version (Visier, HUD-Ring, Leuchtkanten), dezente Glitch-Störungen an Logo und Namen
        // (alle paar Sekunden ein kurzer Ruck mit Farbversatz, beim Drüberfahren sofort), Neonkante an der Seitenleiste,
        // große Launcher-Logos und Launcher-Farben für die Präsentation in der Bibliothek.
        // Glitches nur mit Seiten-Animationen und ohne Leistungsmodus.

        private readonly List<(WeakReference<FrameworkElement> Target, Action<Random> Burst)> glitchTargets = new();
        private readonly DispatcherTimer glitchTimer = new() { Interval = TimeSpan.FromSeconds(3) };
        private readonly Random glitchRandom = new();
        private bool cyberReady;

        private bool GlitchAllowed => IsBrutal && settings.PageAnimations && !settings.PerformanceMode;

        /// <summary>So lange wandert der Störbalken über das Logo; danach folgt das kurze Verzerren (auch am Namen).</summary>
        private const double GlitchSweepMs = 420;

        private void InitCyber()
        {
            if (!IsBrutal || cyberReady) return;
            cyberReady = true;

            MakeGlitchText(BrandTitle);
            MakeGlitchText(NavDockBrandTitle);
            ApplyBrand();   // Pudel in der Neon-Version einsetzen (Seitenleiste und Kopfleiste)
            BuildCyberColorsCard();   // Einstellungen → Darstellung: eigene Farben von Thema 2

            BrandLogo.MouseEnter += (s, e) => GlitchBurst();
            glitchTimer.Tick += (s, e) =>
            {
                glitchTimer.Interval = TimeSpan.FromSeconds(5 + glitchRandom.NextDouble() * 7);
                if (IsVisible && WindowState != WindowState.Minimized && IsActive) GlitchBurst();
            };
            glitchTimer.Start();
        }

        /// <summary>Pudel-Logo passend zum Thema: in Thema 2 die Neon-Version, sonst das Original.</summary>
        private FrameworkElement BrandPoodle() => IsBrutal ? BuildCyberPoodle() : (FrameworkElement)FindResource("DfpLogo");

        /// <summary>Ist der Inhalt schon das Pudel-Logo des aktiven Themas? (Das Original steckt ebenfalls in einer Viewbox.)</summary>
        private bool IsThemePoodle(object? content)
            => content is Viewbox box && Equals(box.Tag, "cyber") == IsBrutal;

        // ───────── Glitch ─────────

        private void RegisterGlitch(FrameworkElement target, Action<Random> burst)
        {
            glitchTargets.RemoveAll(t => !t.Target.TryGetTarget(out _));
            glitchTargets.Add((new WeakReference<FrameworkElement>(target), burst));
        }

        /// <summary>Ein kurzer Glitch an allen sichtbaren Logos und Namen.</summary>
        private void GlitchBurst()
        {
            if (!GlitchAllowed) return;
            foreach (var (reference, burst) in glitchTargets.ToList())
            {
                if (!reference.TryGetTarget(out var target) || !target.IsVisible) continue;
                try { burst(glitchRandom); }
                catch { }
            }
        }

        private static DoubleAnimationUsingKeyFrames Steps(params (double Ms, double Value)[] frames)
        {
            var animation = new DoubleAnimationUsingKeyFrames();
            foreach (var (ms, value) in frames)
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
            return animation;
        }

        /// <summary>
        /// Legt hinter einen Text zwei Farbkopien (Cyan, Magenta), die beim Glitch versetzt aufblitzen.
        /// Text, Größe, Schrift und Sichtbarkeit folgen dem Original automatisch.
        /// </summary>
        private void MakeGlitchText(TextBlock? main)
        {
            if (main?.Parent is not System.Windows.Controls.Panel parent || parent is Grid { Tag: "glitch" }) return;

            int index = parent.Children.IndexOf(main);
            parent.Children.RemoveAt(index);

            var shake = new TranslateTransform();
            var host = new Grid { Tag = "glitch", RenderTransform = shake };

            TextBlock Ghost(string hex)
            {
                var ghost = new TextBlock { Foreground = MakeBrush(hex), Opacity = 0, IsHitTestVisible = false, RenderTransform = new TranslateTransform() };
                foreach (var property in new[]
                {
                    TextBlock.TextProperty, TextBlock.FontSizeProperty, TextBlock.FontWeightProperty, TextBlock.FontFamilyProperty,
                    UIElement.VisibilityProperty, FrameworkElement.MarginProperty
                })
                {
                    ghost.SetBinding(property, new System.Windows.Data.Binding { Source = main, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
                }
                return ghost;
            }

            var cyan = Ghost("#22D3EE");
            var magenta = Ghost("#FF2BD6");
            host.Children.Add(cyan);
            host.Children.Add(magenta);
            host.Children.Add(main);
            parent.Children.Insert(index, host);

            // Der Name verzerrt sich zeitgleich mit dem Logo (nachdem der Störbalken durch ist)
            RegisterGlitch(host, random =>
            {
                var start = TimeSpan.FromMilliseconds(GlitchSweepMs);
                double d = 2 + random.NextDouble() * 2.5;
                foreach (var (ghost, sign) in new[] { (cyan, -1.0), (magenta, 1.0) })
                {
                    var flash = Steps((0, 0.85), (70, 0.25), (120, 0.8), (210, 0.4), (270, 0));
                    flash.BeginTime = start;
                    ghost.BeginAnimation(UIElement.OpacityProperty, flash);
                    var offset = Steps((0, sign * d), (70, -sign * d * 0.5), (120, sign * d * 1.4), (210, sign * d * 0.6), (270, 0));
                    offset.BeginTime = start;
                    ((TranslateTransform)ghost.RenderTransform).BeginAnimation(TranslateTransform.XProperty, offset);
                }
                var jolt = Steps((0, 0), (40, 1.6), (100, -1.4), (160, 0.8), (230, 0));
                jolt.BeginTime = start;
                shake.BeginAnimation(TranslateTransform.XProperty, jolt);
            });
        }

        // ───────── Willkommensanimation in Thema 2 ─────────

        private bool cyberWelcomeReady;

        /// <summary>
        /// Startlogo passend zum Thema: dunkler Holo-Hintergrund statt Thema-1-Fläche, Neon-Pudel (bzw. eigenes Logo),
        /// Name in der Cyber-Schrift mit Farbversatz, Lichtlinie Akzent → Cyan, kurz nach dem Erscheinen ein Glitch.
        /// </summary>
        private void ApplyCyberWelcome(bool reduced)
        {
            if (WelcomeLayer == null) return;

            if (!cyberWelcomeReady)
            {
                cyberWelcomeReady = true;
                WelcomeLayer.Background = ConsoleInk;
                WelcomeLayer.Children.Insert(0, BuildHoloBackdrop());

                WelcomeGreeting.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                WelcomeGreeting.Foreground = ConsoleCyan;
                WelcomeName.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                WelcomeName.FontWeight = FontWeights.Bold;
                if (!settings.PerformanceMode) WelcomeName.SetResourceReference(UIElement.EffectProperty, "TitleGlow");
                MakeGlitchText(WelcomeName);

                var accent = GetAccentColor();
                var line = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
                line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B), 0));
                line.GradientStops.Add(new GradientStop(accent, 0.35));
                line.GradientStops.Add(new GradientStop(HoloCyan, 0.75));
                line.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, HoloCyan.R, HoloCyan.G, HoloCyan.B), 1));
                WelcomeLine.Background = line;
                WelcomeLine.CornerRadius = new CornerRadius(0);
                WelcomeLine.Height = 2;
                if (!settings.PerformanceMode) WelcomeLine.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            }

            // Logo jedes Mal neu (eigenes Logo aus den Einstellungen bleibt erhalten)
            WelcomeLogo.Content = HasBrandImage && BrandLogo?.Content is Border custom
                ? new Border { Background = custom.Background, CornerRadius = custom.CornerRadius }
                : BuildCyberPoodle();
            WelcomeLogo.Width = 190;
            WelcomeLogo.Height = 190;

            // Kurz nach dem Erscheinen: Störbalken über das Logo und Farbversatz am Namen
            if (!reduced)
            {
                var glitch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                glitch.Tick += (s, e) =>
                {
                    glitch.Stop();
                    if (WelcomeLayer.Visibility == Visibility.Visible) GlitchBurstForced();
                };
                glitch.Start();
            }
        }

        /// <summary>Glitch auch dann, wenn das Fenster (noch) nicht aktiv ist – für die Willkommensanimation.</summary>
        private void GlitchBurstForced()
        {
            if (settings.PerformanceMode) return;
            foreach (var (reference, burst) in glitchTargets.ToList())
            {
                if (!reference.TryGetTarget(out var target) || !target.IsVisible) continue;
                try { burst(glitchRandom); }
                catch { }
            }
        }

        // ───────── Pudel in Neon ─────────

        /// <summary>Das Pudel-Logo als Cyberpunk-Version: dunkle Scheibe, Neonkanten, Visier statt Augen, HUD-Ring.</summary>
        private Viewbox BuildCyberPoodle()
        {
            // Zwei Ebenen: „canvas“ ist das Bild, darüber liegen Störbalken und Farbkopien (sie zeigen das Bild über einen VisualBrush)
            var canvas = new Canvas { Width = 500, Height = 500 };
            var shake = new TranslateTransform();
            var skew = new SkewTransform();
            var stretch = new ScaleTransform(1, 1);
            canvas.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            canvas.RenderTransform = new TransformGroup { Children = { stretch, skew, shake } };
            bool effects = !settings.PerformanceMode;
            var accent = GetAccentColor();
            var cyan = MakeBrush("#22D3EE");
            var cyanSoft = MakeBrush("#67E8F9");

            T Add<T>(T element, double left = double.NaN, double top = double.NaN) where T : UIElement
            {
                if (!double.IsNaN(left)) Canvas.SetLeft(element, left);
                if (!double.IsNaN(top)) Canvas.SetTop(element, top);
                canvas.Children.Add(element);
                return element;
            }

            System.Windows.Shapes.Path Shape(string data, System.Windows.Media.Brush? fill, System.Windows.Media.Brush? stroke, double thickness) => new()
            {
                Data = Geometry.Parse(data),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            System.Windows.Shapes.Ellipse Oval(double width, double height, System.Windows.Media.Brush fill, System.Windows.Media.Brush? stroke, double thickness)
                => new() { Width = width, Height = height, Fill = fill, Stroke = stroke, StrokeThickness = thickness };

            // Scheibe mit Ring in der Akzentfarbe
            var disc = new RadialGradientBrush { GradientOrigin = new System.Windows.Point(0.5, 0.32), Center = new System.Windows.Point(0.5, 0.4), RadiusX = 0.66, RadiusY = 0.66 };
            disc.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x1C, 0x26, 0x3D), 0));
            disc.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x0A, 0x0E, 0x17), 0.75));
            disc.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromRgb(0x06, 0x08, 0x0D), 1));
            var ring = Add(Oval(488, 488, disc, null, 9), 6, 6);
            ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");

            // HUD-Ring (gestrichelt, dreht sich ganz langsam) und vier Markierungen
            var hudTurn = new RotateTransform();
            var hud = Add(new System.Windows.Shapes.Ellipse
            {
                Width = 440, Height = 440, Stroke = cyan, StrokeThickness = 3, Opacity = 0.5,
                StrokeDashArray = new DoubleCollection { 3, 5 },
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = hudTurn
            }, 30, 30);
            foreach (string tick in new[] { "M250,18 L250,42", "M250,458 L250,482", "M18,250 L42,250", "M458,250 L482,250" })
                Add(Shape(tick, null, cyan, 6));

            // Körper, Ohren, Kopf und Frisur: dunkel gefüllt mit Neonkanten
            var fur = MakeBrush("#172033");
            var furLight = MakeBrush("#1F2A42");
            Add(Shape("M 140 460 C 140 380, 200 350, 250 350 C 300 350, 360 380, 360 460 Z", fur, cyan, 5));
            Add(Shape("M 130 180 C 100 200, 110 320, 160 310 C 190 300, 180 220, 160 180 Z", MakeBrush("#131B2B"), cyan, 5));
            Add(Shape("M 370 180 C 400 200, 390 320, 340 310 C 310 300, 320 220, 340 180 Z", MakeBrush("#131B2B"), cyan, 5));
            Add(Oval(170, 170, fur, cyan, 5), 165, 115);
            Add(Oval(130, 90, furLight, cyanSoft, 4), 185, 70);
            Add(Oval(80, 60, furLight, cyanSoft, 3), 180, 95);
            Add(Oval(80, 60, furLight, cyanSoft, 3), 240, 95);
            Add(Oval(76, 56, MakeBrush("#243049"), cyan, 3), 212, 207);
            Add(Shape("M 235 220 Q 250 215 265 220 Q 268 235 250 242 Q 232 235 235 220 Z", cyan, null, 0));
            Add(Shape("M 240 249 Q 250 256 260 249", null, cyanSoft, 3));

            // Visier statt Augen: Verlauf Akzent → Cyan, leuchtend
            var visorFill = new LinearGradientBrush(accent, HoloCyan, 0);
            var visor = Add(Shape("M 182 168 L 320 168 L 309 203 L 193 203 Z", visorFill, MakeBrush("#CCFFFFFF"), 2));
            if (effects) visor.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9, Color = HoloCyan };
            Add(Shape("M 196 177 L 300 177", null, MakeBrush("#99FFFFFF"), 3));
            Add(Shape("M 206 194 L 236 194", null, MakeBrush("#55FFFFFF"), 2));

            // Halsband in der Akzentfarbe, Marke mit „DFP“ in Neon
            var collar = Add(Shape("M 165 340 Q 250 410 335 340", null, null, 16));
            collar.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
            Add(new System.Windows.Shapes.Rectangle { Width = 93, Height = 58, RadiusX = 6, RadiusY = 6, Fill = MakeBrush("#0B0F18"), Stroke = cyan, StrokeThickness = 4 }, 203.5, 358.5);
            var tag = Add(new TextBlock { Text = "DFP", Width = 90, TextAlignment = TextAlignment.Center, FontSize = 31, FontWeight = FontWeights.Bold, Foreground = cyan }, 205, 366);
            tag.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            // ── Glitch-Ebene ──
            var fx = new Canvas { Width = 500, Height = 500, IsHitTestVisible = false };

            // Farbkopien (Umriss des Logos in Cyan und Magenta), blitzen beim Verzerren versetzt auf
            System.Windows.Shapes.Rectangle Ghost(string hex) => new()
            {
                Width = 500, Height = 500, Opacity = 0, Fill = MakeBrush(hex),
                OpacityMask = new VisualBrush(canvas)
                {
                    ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 500, 500),
                    ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 500, 500),
                    Stretch = Stretch.Fill
                },
                RenderTransform = new TranslateTransform()
            };
            // Sie liegen hinter dem Bild: zu sehen sind nur die versetzten Ränder
            var ghostCyan = Ghost("#22D3EE");
            var ghostMagenta = Ghost("#FF2BD6");
            var back = new Canvas { Width = 500, Height = 500, IsHitTestVisible = false };
            back.Children.Add(ghostCyan);
            back.Children.Add(ghostMagenta);

            // Störbalken: zeigt den Bildstreifen darunter seitlich verschoben, mit Neonkanten oben und unten
            const double BandHeight = 54;
            var bandImage = new VisualBrush(canvas)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, -BandHeight, 500, BandHeight),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 500, BandHeight),
                Stretch = Stretch.Fill
            };
            var bandShift = new TranslateTransform();
            var band = new Grid { Width = 500, Height = BandHeight, Background = bandImage, Opacity = 0, RenderTransform = bandShift };
            var bandTint = new Border { Opacity = 0.22 };
            bandTint.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            band.Children.Add(bandTint);
            var bandTop = new Border { Height = 3, VerticalAlignment = System.Windows.VerticalAlignment.Top };
            bandTop.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            band.Children.Add(bandTop);
            band.Children.Add(new Border { Height = 2, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Background = cyan });
            Canvas.SetTop(band, -BandHeight);
            fx.Children.Add(band);

            // Bild und Störbalken rund beschneiden, die Farbkopien dahinter nicht (sonst wären ihre Ränder weg)
            canvas.Clip = new EllipseGeometry(new System.Windows.Point(250, 250), 250, 250);
            fx.Clip = new EllipseGeometry(new System.Windows.Point(250, 250), 250, 250);
            var root = new Grid { Width = 500, Height = 500 };
            root.Children.Add(back);
            root.Children.Add(canvas);
            root.Children.Add(fx);
            var box = new Viewbox { Stretch = Stretch.Uniform, Child = root, Tag = "cyber" };

            // Drehen nur, solange das Logo angezeigt wird (sonst hielte die endlose Animation es im Speicher)
            if (effects && settings.PageAnimations)
            {
                box.Loaded += (s, e) => hudTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(48)) { RepeatBehavior = RepeatBehavior.Forever });
                box.Unloaded += (s, e) => hudTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            RegisterGlitch(box, random =>
            {
                // 1) Störbalken wandert von oben nach unten über das Bild (der Streifen darunter ruckelt seitlich)
                var sweep = TimeSpan.FromMilliseconds(GlitchSweepMs);
                Canvas.SetTop(band, -BandHeight);
                band.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(-BandHeight, 500, sweep));
                bandImage.BeginAnimation(VisualBrush.ViewboxProperty, new RectAnimation(new Rect(0, -BandHeight, 500, BandHeight), new Rect(0, 500, 500, BandHeight), sweep));
                band.BeginAnimation(UIElement.OpacityProperty, Steps((0, 1), (GlitchSweepMs, 0)));
                double kick = 14 + random.NextDouble() * 10;
                bandShift.BeginAnimation(TranslateTransform.XProperty, Steps(
                    (0, kick), (70, -kick * 0.6), (140, kick * 1.2), (210, -kick * 0.4), (280, kick * 0.8), (350, -kick), (GlitchSweepMs, 0)));

                // 2) Danach kurz verzerren: Schrägziehen, Strecken, Ruck und Farbversatz
                double t = GlitchSweepMs;
                double tilt = (random.Next(2) == 0 ? 1 : -1) * (7 + random.NextDouble() * 5);
                skew.BeginAnimation(SkewTransform.AngleXProperty, Steps((0, 0), (t, tilt), (t + 50, -tilt * 0.7), (t + 100, tilt * 0.4), (t + 160, 0)));
                stretch.BeginAnimation(ScaleTransform.ScaleXProperty, Steps((0, 1), (t, 1.06), (t + 50, 0.96), (t + 110, 1.02), (t + 160, 1)));
                stretch.BeginAnimation(ScaleTransform.ScaleYProperty, Steps((0, 1), (t, 0.95), (t + 70, 1.03), (t + 160, 1)));
                shake.BeginAnimation(TranslateTransform.XProperty, Steps((0, 0), (t, 10), (t + 50, -8), (t + 100, 5), (t + 160, 0)));
                foreach (var (ghost, sign) in new[] { (ghostCyan, -1.0), (ghostMagenta, 1.0) })
                {
                    ghost.BeginAnimation(UIElement.OpacityProperty, Steps((0, 0), (t, 0.7), (t + 60, 0.25), (t + 110, 0.6), (t + 170, 0)));
                    ((TranslateTransform)ghost.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                        Steps((0, 0), (t, sign * 14), (t + 60, -sign * 8), (t + 110, sign * 18), (t + 170, 0)));
                }
                visor.BeginAnimation(UIElement.OpacityProperty, Steps((0, 1), (t, 0.3), (t + 60, 1), (t + 110, 0.5), (t + 170, 1)));
            });
            return box;
        }

        // ───────── Seitenleiste ─────────

        /// <summary>Neonkante an der Seitenleiste (Akzent oben, Cyan unten).</summary>
        private void ApplyCyberSidebarEdge()
        {
            if (!IsBrutal || SidebarBorder == null) return;
            var accent = GetAccentColor();
            var edge = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xAA, accent.R, accent.G, accent.B), 0));
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x30, 0x30, 0x3F, 0x5A), 0.45));
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x30, 0x30, 0x3F, 0x5A), 0.65));
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, HoloCyan.R, HoloCyan.G, HoloCyan.B), 1));
            SidebarBorder.BorderBrush = edge;
        }

        // ───────── Launcher: großes Logo und Farbe ─────────

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHDefExtractIconW(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint size);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        private readonly Dictionary<string, ImageSource?> launcherLargeIcons = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Logo des Launchers in groß (256 px aus der .exe, wenn möglich), sonst das normale Logo.</summary>
        private ImageSource? GetLauncherLogoLarge(string source)
        {
            if (launcherLargeIcons.TryGetValue(source, out var cached)) return cached;

            ImageSource? logo = null;
            try
            {
                string? exe = LauncherExePath(source);
                if (exe != null && SHDefExtractIconW(exe, 0, 0, out IntPtr large, out IntPtr small, (16u << 16) | 256u) == 0)
                {
                    try
                    {
                        if (large != IntPtr.Zero)
                        {
                            var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                            bitmap.Freeze();
                            logo = bitmap;
                        }
                    }
                    finally
                    {
                        if (large != IntPtr.Zero) DestroyIcon(large);
                        if (small != IntPtr.Zero) DestroyIcon(small);
                    }
                }
            }
            catch { }

            logo ??= GetLauncherIcon(source);
            launcherLargeIcons[source] = logo;
            return logo;
        }

        /// <summary>Typische Farbe des Launchers (für das Leuchten bei der Präsentation), sonst die Akzentfarbe.</summary>
        private System.Windows.Media.Color LauncherTint(string source)
        {
            string? hex = source switch
            {
                "Steam" => "#66C0F4",
                "Epic Games" => "#E8E8E8",
                "Xbox" => "#16C60C",
                "EA" => "#FF4747",
                "Ubisoft" => "#2C8CFF",
                "GOG" => "#B05CF5",
                "Battle.net" => "#00AEFF",
                "Rockstar" => "#FCAF17",
                "Amazon Games" => "#FF9900",
                "Riot Games" => "#EB0029",
                "itch.io" => "#FA5C5C",
                "HoYoPlay" => "#5EC8F8",
                _ => null
            };
            return hex != null ? ParseColor(hex, "#22D3EE") : GetAccentColor();
        }
    }
}
