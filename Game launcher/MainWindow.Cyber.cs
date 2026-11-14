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

        private void InitCyber()
        {
            if (!IsBrutal || cyberReady) return;
            cyberReady = true;

            MakeGlitchText(BrandTitle);
            MakeGlitchText(NavDockBrandTitle);
            ApplyBrand();   // Pudel in der Neon-Version einsetzen (Seitenleiste und Kopfleiste)

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

            RegisterGlitch(host, random =>
            {
                double d = 2 + random.NextDouble() * 2.5;
                foreach (var (ghost, sign) in new[] { (cyan, -1.0), (magenta, 1.0) })
                {
                    ghost.BeginAnimation(UIElement.OpacityProperty, Steps((0, 0.85), (70, 0.25), (120, 0.8), (210, 0.4), (270, 0)));
                    ((TranslateTransform)ghost.RenderTransform).BeginAnimation(TranslateTransform.XProperty,
                        Steps((0, sign * d), (70, -sign * d * 0.5), (120, sign * d * 1.4), (210, sign * d * 0.6), (270, 0)));
                }
                shake.BeginAnimation(TranslateTransform.XProperty, Steps((0, 0), (40, 1.6), (100, -1.4), (160, 0.8), (230, 0)));
            });
        }

        // ───────── Pudel in Neon ─────────

        /// <summary>Das Pudel-Logo als Cyberpunk-Version: dunkle Scheibe, Neonkanten, Visier statt Augen, HUD-Ring.</summary>
        private Viewbox BuildCyberPoodle()
        {
            var canvas = new Canvas { Width = 500, Height = 500, Clip = new EllipseGeometry(new System.Windows.Point(250, 250), 250, 250) };
            var shake = new TranslateTransform();
            canvas.RenderTransform = shake;
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
            var visorFill = new LinearGradientBrush(accent, System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE), 0);
            var visor = Add(Shape("M 182 168 L 320 168 L 309 203 L 193 203 Z", visorFill, MakeBrush("#CCFFFFFF"), 2));
            if (effects) visor.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 22, ShadowDepth = 0, Opacity = 0.9, Color = System.Windows.Media.Color.FromRgb(0x22, 0xD3, 0xEE) };
            Add(Shape("M 196 177 L 300 177", null, MakeBrush("#99FFFFFF"), 3));
            Add(Shape("M 206 194 L 236 194", null, MakeBrush("#55FFFFFF"), 2));

            // Halsband in der Akzentfarbe, Marke mit „DFP“ in Neon
            var collar = Add(Shape("M 165 340 Q 250 410 335 340", null, null, 16));
            collar.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "AccentBrush");
            Add(new System.Windows.Shapes.Rectangle { Width = 93, Height = 58, RadiusX = 6, RadiusY = 6, Fill = MakeBrush("#0B0F18"), Stroke = cyan, StrokeThickness = 4 }, 203.5, 358.5);
            var tag = Add(new TextBlock { Text = "DFP", Width = 90, TextAlignment = TextAlignment.Center, FontSize = 31, FontWeight = FontWeights.Bold, Foreground = cyan }, 205, 366);
            tag.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");

            // Glitch-Streifen (sonst unsichtbar)
            var sliceA = Add(new System.Windows.Shapes.Rectangle { Width = 500, Height = 24, Opacity = 0, IsHitTestVisible = false, RenderTransform = new TranslateTransform() }, 0, 0);
            sliceA.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush");
            var sliceB = Add(new System.Windows.Shapes.Rectangle { Width = 500, Height = 14, Opacity = 0, IsHitTestVisible = false, Fill = cyan, RenderTransform = new TranslateTransform() }, 0, 0);

            var box = new Viewbox { Stretch = Stretch.Uniform, Child = canvas, Tag = "cyber" };

            // Drehen nur, solange das Logo angezeigt wird (sonst hielte die endlose Animation es im Speicher)
            if (effects && settings.PageAnimations)
            {
                box.Loaded += (s, e) => hudTurn.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(48)) { RepeatBehavior = RepeatBehavior.Forever });
                box.Unloaded += (s, e) => hudTurn.BeginAnimation(RotateTransform.AngleProperty, null);
            }

            RegisterGlitch(box, random =>
            {
                shake.BeginAnimation(TranslateTransform.XProperty, Steps((0, 0), (40, 9), (90, -7), (150, 4), (220, 0)));
                foreach (var slice in new[] { sliceA, sliceB })
                {
                    Canvas.SetTop(slice, 60 + random.Next(0, 360));
                    slice.BeginAnimation(UIElement.OpacityProperty, Steps((0, 0.55), (60, 0), (110, 0.4), (190, 0)));
                    ((TranslateTransform)slice.RenderTransform).BeginAnimation(TranslateTransform.XProperty, Steps((0, random.Next(-30, 30)), (110, random.Next(-30, 30)), (190, 0)));
                }
                visor.BeginAnimation(UIElement.OpacityProperty, Steps((0, 0.35), (60, 1), (120, 0.5), (200, 1)));
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
            edge.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x99, 0x22, 0xD3, 0xEE), 1));
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
