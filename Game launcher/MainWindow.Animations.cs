using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Game_launcher
{
    // ───────────────────────────── Animationen: Spielstart, Durchgespielt-Stempel, Controller-Lichtstreifen ─────────────────────────────

    public partial class MainWindow
    {
        /// <summary>Große Effekt-Animationen nur mit Seitenanimationen und ohne Leistungsmodus.</summary>
        private bool FancyAnimations => settings.PageAnimations && !settings.PerformanceMode;

        private bool CanShowOverlayAnimation => FancyAnimations && IsVisible && WindowState != WindowState.Minimized && WindowRoot != null;

        private static readonly Duration OverlayFade = new(TimeSpan.FromMilliseconds(380));

        private Grid CreateOverlayHost(double dim)
        {
            var host = new Grid { Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(255 * dim), 4, 5, 9)), Opacity = 0 };
            System.Windows.Controls.Panel.SetZIndex(host, 155);   // über dem Controller-Modus (150), unter den Meldungen (160)
            WindowRoot.Children.Add(host);
            host.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            return host;
        }

        private void FadeOutOverlay(Grid host, Action? done = null)
        {
            if (!WindowRoot.Children.Contains(host)) { done?.Invoke(); return; }
            host.IsHitTestVisible = false;
            var fade = new DoubleAnimation(0, OverlayFade) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (s, e) =>
            {
                WindowRoot.Children.Remove(host);
                done?.Invoke();
            };
            host.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private Border CreateOverlayCover(GameItem game, double width, double height)
        {
            var surface = new Grid();
            surface.Children.Add(new TextBlock
            {
                Text = game.Name.Length > 0 ? game.Name.Substring(0, 1).ToUpperInvariant() : "?",
                FontSize = width * 0.34,
                FontWeight = FontWeights.Bold,
                Foreground = BrushPlaceholder,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage image)
            {
                var picture = new System.Windows.Controls.Image { Source = image, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
                surface.Children.Add(picture);
            }

            return new Border
            {
                Width = width,
                Height = height,
                CornerRadius = new CornerRadius(18),
                Background = BrushCardBg,
                Child = surface,
                Clip = new RectangleGeometry(new System.Windows.Rect(0, 0, width, height), 18, 18),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };
        }

        private static System.Windows.Media.Color Lighten(System.Windows.Media.Color color, double amount)
        {
            static byte Mix(byte value, double amount) => (byte)Math.Round(value + (255 - value) * amount);
            return System.Windows.Media.Color.FromRgb(Mix(color.R, amount), Mix(color.G, amount), Mix(color.B, amount));
        }

        // ───────────────────────────── Spielstart ─────────────────────────────

        private Grid? launchOverlay;
        private GameItem? launchOverlayGame;
        private TextBlock? launchOverlayStatus;
        private UIElement? launchOverlaySpinner;
        private Border? launchOverlayCover;
        private DispatcherTimer? launchOverlayTimer;
        private bool launchOverlayMinimize;
        private DateTime launchOverlayShown;

        private static readonly TimeSpan LaunchOverlayMinimum = TimeSpan.FromMilliseconds(1500);
        private static readonly TimeSpan LaunchOverlayMaximum = TimeSpan.FromSeconds(7);

        /// <summary>Cover fliegt in die Mitte, Ladering läuft, bis das Spiel erkannt wird. Liefert false, wenn keine Animation läuft.</summary>
        private bool ShowLaunchAnimation(GameItem game)
        {
            if (!CanShowOverlayAnimation) return false;
            CloseLaunchAnimation(false);

            var host = CreateOverlayHost(0.82);
            host.MouseLeftButtonUp += (s, e) => CloseLaunchAnimation(true);
            launchOverlay = host;
            launchOverlayGame = game;
            launchOverlayMinimize = false;
            launchOverlayShown = DateTime.Now;

            if (IsBrutal && settings.CyberLaunchHud)
            {
                // Thema 2: Lade-HUD (MainWindow.CyberFx.cs)
                BuildCyberLaunchHud(host, game);
                launchOverlayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                launchOverlayTimer.Tick -= LaunchOverlayTick;
                launchOverlayTimer.Tick += LaunchOverlayTick;
                launchOverlayTimer.Start();
                return true;
            }

            var accent = GetAccentColor();
            double coverWidth = 240, coverHeight = 360;

            // Farbiges Leuchten hinter dem Cover
            var glow = new Ellipse
            {
                Width = 720,
                Height = 720,
                Fill = new RadialGradientBrush(System.Windows.Media.Color.FromArgb(0x70, accent.R, accent.G, accent.B), System.Windows.Media.Color.FromArgb(0, accent.R, accent.G, accent.B)),
                IsHitTestVisible = false,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(0.4, 0.4),
                Opacity = 0
            };
            host.Children.Add(glow);

            var stack = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            host.Children.Add(stack);

            var cover = CreateOverlayCover(game, coverWidth, coverHeight);
            var coverScale = new ScaleTransform(0.35, 0.35);
            var coverMove = new TranslateTransform();
            cover.RenderTransform = new TransformGroup { Children = { coverScale, coverMove } };
            cover.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 60, ShadowDepth = 0, Opacity = 0.55 };
            launchOverlayCover = cover;
            stack.Children.Add(cover);

            var title = new TextBlock
            {
                Text = game.Name,
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 560,
                Margin = new Thickness(0, 30, 0, 0),
                Opacity = 0
            };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            stack.Children.Add(title);

            var statusRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Opacity = 0 };
            var spinner = new Ellipse
            {
                Width = 18,
                Height = 18,
                Stroke = new SolidColorBrush(Lighten(accent, 0.25)),
                StrokeThickness = 2.5,
                StrokeDashArray = new DoubleCollection { 14, 30 },
                StrokeDashCap = PenLineCap.Round,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new RotateTransform(),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            ((RotateTransform)spinner.RenderTransform).BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850)) { RepeatBehavior = RepeatBehavior.Forever });
            launchOverlaySpinner = spinner;
            statusRow.Children.Add(spinner);

            var status = new TextBlock { Text = Loc.T("Spiel wird gestartet ..."), FontSize = 15, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            status.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            launchOverlayStatus = status;
            statusRow.Children.Add(status);
            stack.Children.Add(statusRow);

            // Startpunkt: dort, wo geklickt wurde (sonst aus der Mitte)
            double fromX = 0, fromY = 0;
            if (!controllerMode && IsActive)
            {
                var mouse = System.Windows.Input.Mouse.GetPosition(WindowRoot);
                if (mouse.X >= 0 && mouse.Y >= 0 && mouse.X <= WindowRoot.ActualWidth && mouse.Y <= WindowRoot.ActualHeight)
                {
                    fromX = mouse.X - WindowRoot.ActualWidth / 2;
                    fromY = mouse.Y - WindowRoot.ActualHeight / 2 + 40;
                }
            }

            var fly = TimeSpan.FromMilliseconds(560);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
            coverMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, fly) { EasingFunction = ease });
            coverMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, fly) { EasingFunction = ease });
            coverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.35, 1, fly) { EasingFunction = pop });
            coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.35, 1, fly) { EasingFunction = pop });

            var glowScale = (ScaleTransform)glow.RenderTransform;
            glow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(200) });
            var breathe = new DoubleAnimation(0.85, 1.05, TimeSpan.FromMilliseconds(1400)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, breathe);
            glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, breathe);

            title.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(320) });
            statusRow.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = TimeSpan.FromMilliseconds(460) });

            launchOverlayTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            launchOverlayTimer.Tick -= LaunchOverlayTick;
            launchOverlayTimer.Tick += LaunchOverlayTick;
            launchOverlayTimer.Start();
            return true;
        }

        private void LaunchOverlayTick(object? sender, EventArgs e)
        {
            if (launchOverlay == null) { launchOverlayTimer?.Stop(); return; }

            var shown = DateTime.Now - launchOverlayShown;
            // Soll der Launcher minimieren, nicht auf die Spielerkennung warten
            if ((launchOverlayMinimize && shown >= LaunchOverlayMinimum) || shown >= LaunchOverlayMaximum)
                FinishLaunchAnimation();
        }

        /// <summary>Wird aus der Spielzeit-Erfassung gerufen, sobald das Spiel läuft.</summary>
        private void OnLaunchAnimationGameDetected(GameItem game)
        {
            if (launchOverlay == null || launchOverlayGame != game) return;

            var wait = LaunchOverlayMinimum - (DateTime.Now - launchOverlayShown);
            if (wait > TimeSpan.Zero)
            {
                var delay = new DispatcherTimer { Interval = wait };
                delay.Tick += (s, e) => { delay.Stop(); if (launchOverlayGame == game) FinishLaunchAnimation(); };
                delay.Start();
            }
            else FinishLaunchAnimation();
        }

        /// <summary>„Viel Spaß!“, Cover wächst kurz, dann weich ausblenden (und danach minimieren, falls eingestellt).</summary>
        private void FinishLaunchAnimation()
        {
            var host = launchOverlay;
            if (host == null) return;
            launchOverlayTimer?.Stop();

            if (launchOverlayStatus != null) launchOverlayStatus.Text = Loc.T("Viel Spaß!");
            launchOverlaySpinner?.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            if (launchOverlayCover?.RenderTransform is TransformGroup { Children: [ScaleTransform scale, ..] })
            {
                var grow = new DoubleAnimation(1.06, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }

            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(520) };
            hold.Tick += (s, e) =>
            {
                hold.Stop();
                if (launchOverlay == host) CloseLaunchAnimation(true);
            };
            hold.Start();
        }

        private void CloseLaunchAnimation(bool animated)
        {
            var host = launchOverlay;
            bool minimize = launchOverlayMinimize;
            launchOverlay = null;
            launchOverlayGame = null;
            launchOverlayStatus = null;
            launchOverlaySpinner = null;
            launchOverlayCover = null;
            launchOverlayMinimize = false;
            launchOverlayTimer?.Stop();
            if (host == null) return;

            void Done()
            {
                if (minimize && WindowState != WindowState.Minimized) WindowState = WindowState.Minimized;
            }

            if (animated) FadeOutOverlay(host, Done);
            else
            {
                WindowRoot.Children.Remove(host);
                Done();
            }
        }

        // ───────────────────────────── Stempel „Durchgespielt“ ─────────────────────────────

        private Grid? stampOverlay;

        private void ShowCompletedStamp(GameItem game)
        {
            if (!CanShowOverlayAnimation) return;
            if (stampOverlay != null) WindowRoot.Children.Remove(stampOverlay);

            var host = CreateOverlayHost(0.62);
            stampOverlay = host;
            host.MouseLeftButtonUp += (s, e) => CloseStamp(host);

            var accent = GetAccentColor();
            var light = Lighten(accent, 0.2);

            var stage = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            host.Children.Add(stage);

            // Cover springt herein
            var cover = CreateOverlayCover(game, 260, 390);
            var coverScale = new ScaleTransform(0.85, 0.85);
            var coverShake = new TranslateTransform();
            cover.RenderTransform = new TransformGroup { Children = { coverScale, coverShake } };
            stage.Children.Add(cover);
            var popIn = new DoubleAnimation(0.85, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 } };
            coverScale.BeginAnimation(ScaleTransform.ScaleXProperty, popIn);
            coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, popIn);

            // Das Siegel
            var seal = new Grid { Width = 210, Height = 210, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), Opacity = 0, IsHitTestVisible = false };
            var stroke = new SolidColorBrush(light);
            // Dunkle, leicht eingefärbte Füllung, damit Haken und Datum auf jedem Cover lesbar bleiben
            var sealFill = System.Windows.Media.Color.FromArgb(0xD8, (byte)(accent.R * 0.22 + 8), (byte)(accent.G * 0.22 + 8), (byte)(accent.B * 0.22 + 10));
            seal.Children.Add(new Ellipse { Fill = new SolidColorBrush(sealFill), Stroke = stroke, StrokeThickness = 6 });
            seal.Children.Add(new Ellipse { Margin = new Thickness(13), Stroke = stroke, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2.5 } });

            var inner = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            inner.Children.Add(new TextBlock { Text = "✓", FontSize = 52, FontWeight = FontWeights.Bold, Foreground = stroke, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, -6, 0, -4) });
            var band = new Border
            {
                Background = new SolidColorBrush(accent),
                Padding = new Thickness(10, 4, 10, 5),
                Margin = new Thickness(-26, 0, -26, 0),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = Loc.T("Durchgespielt").ToUpper(CultureInfo.CurrentCulture),
                    FontSize = 19,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                }
            };
            ((TextBlock)band.Child).SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            inner.Children.Add(band);
            inner.Children.Add(new TextBlock { Text = DateTime.Now.ToString("d", CultureInfo.CurrentCulture), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = stroke, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            seal.Children.Add(inner);

            var sealScale = new ScaleTransform(2.6, 2.6);
            seal.RenderTransform = new TransformGroup { Children = { sealScale, new RotateTransform(-14) } };
            stage.Children.Add(seal);

            var particles = new Canvas { IsHitTestVisible = false, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Width = 0, Height = 0 };
            stage.Children.Add(particles);

            // Aufschlag: Siegel knallt herunter
            var slamStart = TimeSpan.FromMilliseconds(380);
            var slam = TimeSpan.FromMilliseconds(210);
            var slamDown = new DoubleAnimation(2.6, 1, slam) { BeginTime = slamStart, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
            slamDown.Completed += (s, e) =>
            {
                if (stampOverlay != host) return;

                // kleiner Nachfederer des Siegels
                var settle = new DoubleAnimationUsingKeyFrames();
                settle.KeyFrames.Add(new EasingDoubleKeyFrame(1.07, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
                settle.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(240)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                sealScale.BeginAnimation(ScaleTransform.ScaleXProperty, settle);
                sealScale.BeginAnimation(ScaleTransform.ScaleYProperty, settle);

                // Cover wackelt beim Aufschlag
                var shake = new DoubleAnimationUsingKeyFrames();
                double[] offsets = { -9, 8, -6, 4, -2, 0 };
                for (int i = 0; i < offsets.Length; i++)
                    shake.KeyFrames.Add(new LinearDoubleKeyFrame(offsets[i], KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(40 * (i + 1)))));
                coverShake.BeginAnimation(TranslateTransform.XProperty, shake);
                var squash = new DoubleAnimationUsingKeyFrames();
                squash.KeyFrames.Add(new LinearDoubleKeyFrame(0.96, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(60))));
                squash.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new BackEase { EasingMode = EasingMode.EaseOut }));
                coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, squash);

                BurstParticles(particles, accent);
            };
            sealScale.BeginAnimation(ScaleTransform.ScaleXProperty, slamDown);
            sealScale.BeginAnimation(ScaleTransform.ScaleYProperty, slamDown.Clone());
            seal.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)) { BeginTime = slamStart });

            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            hold.Tick += (s, e) => { hold.Stop(); CloseStamp(host); };
            hold.Start();
        }

        private void BurstParticles(Canvas canvas, System.Windows.Media.Color accent)
        {
            var palette = new[] { accent, Lighten(accent, 0.45), System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24), System.Windows.Media.Colors.White };
            for (int i = 0; i < 26; i++)
            {
                double angle = (Math.PI * 2 * i / 26) + random.NextDouble() * 0.25;
                double distance = 140 + random.Next(130);
                double size = 5 + random.Next(7);
                var dot = new Ellipse { Width = size, Height = size, Fill = new SolidColorBrush(palette[random.Next(palette.Length)]) };
                var move = new TranslateTransform();
                dot.RenderTransform = move;
                Canvas.SetLeft(dot, -size / 2);
                Canvas.SetTop(dot, -size / 2);
                canvas.Children.Add(dot);

                var duration = TimeSpan.FromMilliseconds(620 + random.Next(380));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(Math.Cos(angle) * 60, Math.Cos(angle) * distance, duration) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Math.Sin(angle) * 60, Math.Sin(angle) * distance + 30, duration) { EasingFunction = ease });
                var fade = new DoubleAnimation(1, 0, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
                fade.Completed += (s, e) => canvas.Children.Remove(dot);
                dot.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        private void CloseStamp(Grid host)
        {
            if (stampOverlay == host) stampOverlay = null;
            FadeOutOverlay(host);
        }

        // ───────────────────────────── Controller-Modus: Lichtstreifen um das ausgewählte Spiel ─────────────────────────────

        private const double StreakThickness = 6;
        private const double StreakRadius = 18;

        /// <summary>Lichtstreifen, die am Rand des Covers entlanglaufen. Nur für die Karte, die gerade im Fokus ist.</summary>
        private System.Windows.Shapes.Rectangle CreateFlowStreak(double width, double height)
        {
            // Umfang in Strichstärken: die Striche von StrokeDashArray zählen in Vielfachen von StrokeThickness
            double perimeter = 2 * (width + height) - (8 - 2 * Math.PI) * StreakRadius;
            double units = perimeter / StreakThickness;
            double segment = units * 0.16;

            var streak = new System.Windows.Shapes.Rectangle
            {
                RadiusX = StreakRadius,
                RadiusY = StreakRadius,
                StrokeThickness = StreakThickness,
                StrokeDashArray = new DoubleCollection { segment, units / 2 - segment },
                StrokeDashCap = PenLineCap.Round,
                IsHitTestVisible = false,
                Opacity = 0,
                Tag = units
            };
            return streak;
        }

        private void FlowStreakFocus(System.Windows.Shapes.Rectangle streak, double amount)
        {
            // Nur im Cover-Flow des Controller-Modus (nicht in der Cover-Flow-Ansicht der Bibliothek)
            bool on = amount > 0.92 && FancyAnimations && padFlow != null && padFlow.IsAncestorOf(streak);
            bool running = streak.Stroke != null;
            streak.Opacity = on ? 1 : 0;
            if (on == running) return;

            if (on)
            {
                // Fast weißer Streifen mit Leuchten in der Akzentfarbe
                var accent = GetAccentColor();
                streak.Stroke = new SolidColorBrush(Lighten(accent, 0.78));
                streak.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Lighten(accent, 0.3), BlurRadius = 16, ShadowDepth = 0, Opacity = 1 };
                double units = streak.Tag is double value ? value : 300;
                streak.BeginAnimation(Shape.StrokeDashOffsetProperty,
                    new DoubleAnimation(0, -units, TimeSpan.FromSeconds(3.2)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                streak.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
                streak.Stroke = null;
                streak.Effect = null;
            }
        }
    }
}
