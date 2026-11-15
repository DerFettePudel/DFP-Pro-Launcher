using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Effekte ─────────────────────────────
        //
        // Klänge (Hover, Klick, Öffnen, Start), lebender Hintergrund (Punkteraster wandert und folgt der Maus),
        // Seitenwechsel als Scan-Linie, Lade-HUD beim Spielstart, Begrüßung/Status nach Tageszeit (nachts gedämpft).
        // Alles einzeln abschaltbar (Einstellungen → Darstellung → Effekte von Thema 2), Leistungsmodus schaltet die
        // teuren Teile ab, im Streamer-Modus sind die Klänge stumm.

        // ───────── Klänge ─────────

        private readonly Dictionary<string, System.Media.SoundPlayer> cyberSoundCache = new();
        private int cyberSoundCacheVolume = -1;
        private DateTime cyberLastHoverSound;

        private void PlayCyberSound(string kind)
        {
            if (!IsConsole || !settings.CyberSounds || settings.CyberSoundVolume <= 0 || StreamerOn || AlertsMuted()) return;
            if (kind == "hover")
            {
                if ((DateTime.Now - cyberLastHoverSound).TotalMilliseconds < 90) return;
                cyberLastHoverSound = DateTime.Now;
            }
            try
            {
                if (cyberSoundCacheVolume != settings.CyberSoundVolume)
                {
                    foreach (var old in cyberSoundCache.Values) old.Dispose();
                    cyberSoundCache.Clear();
                    cyberSoundCacheVolume = settings.CyberSoundVolume;
                }
                if (!cyberSoundCache.TryGetValue(kind, out var player))
                {
                    player = new System.Media.SoundPlayer(new MemoryStream(MakeCyberWav(kind, settings.CyberSoundVolume / 100.0)));
                    player.Load();
                    cyberSoundCache[kind] = player;
                }
                // Abspielen auf einem Hintergrund-Thread: die Oberfläche wartet nie darauf
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { player.Play(); }
                    catch { }
                });
            }
            catch { }
        }

        /// <summary>Synthetische Klänge im Stil von Thema 2 (Sinus-Gleiter, leises Summen), als WAV im Speicher.</summary>
        private static byte[] MakeCyberWav(string kind, double volume)
        {
            const int rate = 22050;
            var samples = new List<double>();
            var random = new Random(7);

            // Ton, der von f0 nach f1 gleitet; Hüllkurve: kurzer Anstieg, exponentielles Abklingen
            void Glide(double f0, double f1, int ms, double amp, double decay, double harmonic = 0)
            {
                int count = rate * ms / 1000;
                double phase = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)count;
                    double f = f0 * Math.Pow(f1 / f0, t);
                    phase += 2 * Math.PI * f / rate;
                    double env = Math.Min(1, i / (rate * 0.003)) * Math.Exp(-decay * t) * Math.Min(1, (count - i) / (rate * 0.004));
                    samples.Add((Math.Sin(phase) + harmonic * Math.Sin(3 * phase)) * amp * env);
                }
            }

            void Silence(int ms) { for (int i = 0; i < rate * ms / 1000; i++) samples.Add(0); }

            switch (kind)
            {
                case "hover":
                    Glide(2300, 3100, 26, 0.22, 5);
                    break;
                case "click":
                    Glide(1400, 1500, 24, 0.45, 3, 0.15);
                    Silence(8);
                    Glide(2100, 2250, 40, 0.38, 4, 0.1);
                    break;
                case "type":
                    {
                        int count = rate * 14 / 1000;
                        for (int i = 0; i < count; i++) samples.Add((random.NextDouble() * 2 - 1) * 0.18 * Math.Exp(-6.0 * i / count));
                    }
                    break;
                case "confirm":
                    Glide(1320, 1320, 55, 0.4, 2.5);
                    Glide(1760, 1760, 110, 0.4, 3.5);
                    break;
                case "open":
                    {
                        // Aufsteigender Gleiter mit leisem Summen darunter
                        int count = rate * 240 / 1000;
                        double p1 = 0, p2 = 0;
                        for (int i = 0; i < count; i++)
                        {
                            double t = i / (double)count;
                            p1 += 2 * Math.PI * (380 * Math.Pow(1150 / 380.0, t)) / rate;
                            p2 += 2 * Math.PI * 110 / rate;
                            double env = Math.Min(1, i / (rate * 0.01)) * (1 - t);
                            samples.Add((Math.Sin(p1) * 0.32 + Math.Sin(p2) * 0.16 * (1 - t)) * env);
                        }
                    }
                    break;
                case "launch":
                    {
                        // Hochfahren: Summen + steigender Gleiter mit Tremolo, am Ende ein heller Ton
                        int count = rate * 620 / 1000;
                        double p1 = 0, p2 = 0;
                        for (int i = 0; i < count; i++)
                        {
                            double t = i / (double)count;
                            p1 += 2 * Math.PI * (180 * Math.Pow(1500 / 180.0, t * t)) / rate;
                            p2 += 2 * Math.PI * 82 / rate;
                            double tremolo = 0.75 + 0.25 * Math.Sin(2 * Math.PI * 18 * t);
                            double env = Math.Min(1, i / (rate * 0.02)) * Math.Min(1, (count - i) / (rate * 0.03));
                            samples.Add((Math.Sin(p1) * 0.3 * tremolo + Math.Sin(p2) * 0.22) * env);
                        }
                        Glide(1760, 1760, 160, 0.34, 4);
                    }
                    break;
                default:
                    Glide(1200, 1200, 40, 0.3, 4);
                    break;
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            int dataLength = samples.Count * 2;
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            double gain = 14000 * Math.Clamp(volume, 0, 1);
            foreach (double sample in samples) writer.Write((short)Math.Clamp(sample * gain, short.MinValue, short.MaxValue));
            writer.Flush();
            return stream.ToArray();
        }

        // ───────── Lebender Hintergrund ─────────

        private bool CyberLiveBackdropOn => settings.CyberLiveBackground && settings.PageAnimations && !settings.PerformanceMode;
        private readonly List<(WeakReference<FrameworkElement> Layer, double Strength)> cyberParallaxLayers = new();
        private DateTime cyberParallaxLast;

        /// <summary>Punkteraster wandert langsam (nahtlos, eine Kachel pro Durchlauf) und die Ebenen folgen leicht der Maus.</summary>
        private void MakeBackdropLive(DrawingBrush dots, FrameworkElement dotLayer, FrameworkElement glowLayer)
        {
            if (!CyberLiveBackdropOn) return;
            var drift = new TranslateTransform();
            dots.Transform = drift;
            var x = new DoubleAnimation(0, 26, TimeSpan.FromSeconds(11)) { RepeatBehavior = RepeatBehavior.Forever };
            var y = new DoubleAnimation(0, 26, TimeSpan.FromSeconds(17)) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(x, 30);
            Timeline.SetDesiredFrameRate(y, 30);
            drift.BeginAnimation(TranslateTransform.XProperty, x);
            drift.BeginAnimation(TranslateTransform.YProperty, y);

            // Rand nach außen erweitern, damit beim Verschieben keine Kante sichtbar wird
            dotLayer.Margin = new Thickness(-30);
            glowLayer.Margin = new Thickness(-60);
            dotLayer.RenderTransform = new TranslateTransform();
            glowLayer.RenderTransform = new TranslateTransform();
            cyberParallaxLayers.RemoveAll(l => !l.Layer.TryGetTarget(out _));
            cyberParallaxLayers.Add((new WeakReference<FrameworkElement>(dotLayer), -14));
            cyberParallaxLayers.Add((new WeakReference<FrameworkElement>(glowLayer), 30));
        }

        /// <summary>Mausbewegung über dem Fenster: Hintergrund-Ebenen gleiten sanft hinterher (höchstens 20-mal pro Sekunde).</summary>
        private void CyberParallax(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!IsConsole || !CyberLiveBackdropOn || cyberParallaxLayers.Count == 0) return;
            if ((DateTime.Now - cyberParallaxLast).TotalMilliseconds < 50) return;
            cyberParallaxLast = DateTime.Now;
            if (WindowRoot == null || WindowRoot.ActualWidth <= 0) return;
            var p = e.GetPosition(WindowRoot);
            double rx = Math.Clamp(p.X / WindowRoot.ActualWidth, 0, 1) - 0.5, ry = Math.Clamp(p.Y / WindowRoot.ActualHeight, 0, 1) - 0.5;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            foreach (var (reference, strength) in cyberParallaxLayers)
            {
                if (!reference.TryGetTarget(out var layer) || !layer.IsVisible || layer.RenderTransform is not TranslateTransform move) continue;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(rx * strength * 2, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(ry * strength * 2, TimeSpan.FromMilliseconds(700)) { EasingFunction = ease });
            }
        }

        // ───────── Seitenwechsel als Scan-Linie ─────────

        private void PlayCyberScan(Grid? host)
        {
            if (host == null || !IsConsole || !settings.CyberScanTransition || !FancyAnimations || host.ActualHeight <= 0) return;

            var layer = new Grid { IsHitTestVisible = false, ClipToBounds = true };
            System.Windows.Controls.Panel.SetZIndex(layer, 999);
            var accent = GetAccentColor();

            // Leuchtband mit heller Linie an der Unterkante, dahinter feine Bildzeilen
            var band = new Grid { Height = 160, VerticalAlignment = System.Windows.VerticalAlignment.Top };
            var fill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x26, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0.85));
            fill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x55, accent.R, accent.G, accent.B), 1));
            band.Children.Add(new Border { Background = fill });
            var scanlines = new DrawingBrush(new GeometryDrawing(new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), null, new RectangleGeometry(new Rect(0, 0, 4, 1))))
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 4, 3),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 4, 3),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            band.Children.Add(new Border { Background = scanlines, OpacityMask = fill });
            var lineFill = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(1, 0) };
            lineFill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, HoloCyan.R, HoloCyan.G, HoloCyan.B), 0));
            lineFill.GradientStops.Add(new GradientStop(HoloCyan, 0.3));
            lineFill.GradientStops.Add(new GradientStop(System.Windows.Media.Colors.White, 0.5));
            lineFill.GradientStops.Add(new GradientStop(accent, 0.7));
            lineFill.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, accent.R, accent.G, accent.B), 1));
            var line = new Border { Height = 2, VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Background = lineFill };
            line.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = HoloCyan, BlurRadius = 18, ShadowDepth = 0, Opacity = 1 };
            band.Children.Add(line);
            layer.Children.Add(band);

            var move = new TranslateTransform(0, -160);
            band.RenderTransform = move;
            host.Children.Add(layer);

            var run = new DoubleAnimation(-160, host.ActualHeight + 10, TimeSpan.FromMilliseconds(560)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            run.Completed += (s, e) => host.Children.Remove(layer);
            move.BeginAnimation(TranslateTransform.YProperty, run);
            layer.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(380) });
        }

        // ───────── Begrüßung nach Tageszeit ─────────

        private bool cyberNightActive;

        /// <summary>Nachts (22–5 Uhr) leuchtet Thema 2 etwas gedämpfter.</summary>
        private bool CyberNightNow => IsBrutal && settings.CyberGreeting && (DateTime.Now.Hour >= 22 || DateTime.Now.Hour < 5);

        /// <summary>Status-Zeile im Terminal-Stil passend zur Tageszeit.</summary>
        private string CyberStatusLine()
        {
            int hour = DateTime.Now.Hour;
            string status = hour switch
            {
                >= 5 and < 11 => "SYSTEM ONLINE",
                >= 11 and < 17 => "ALLE SYSTEME BEREIT",
                >= 17 and < 22 => "ABENDSCHICHT",
                _ => "NACHTMODUS AKTIV"
            };
            return Loc.T(status);
        }

        /// <summary>Jede Minute (Uhr): beim Wechsel in die Nacht oder zurück das Leuchten anpassen.</summary>
        private void CheckCyberNight()
        {
            if (!IsBrutal) return;
            bool night = CyberNightNow;
            if (night == cyberNightActive) return;
            cyberNightActive = night;
            ApplyCyberColorsNow();
        }

        // ───────── Lade-HUD beim Spielstart ─────────

        private DispatcherTimer? cyberHudTimer;

        /// <summary>Thema 2: Statt der normalen Start-Animation ein HUD mit Cover, Name, Sitzungszähler und Systemanzeige.</summary>
        private void BuildCyberLaunchHud(Grid host, GameItem game)
        {
            host.Background = ConsoleInk;
            host.Children.Add(BuildHoloBackdrop());
            PlayCyberSound("launch");

            var accent = GetAccentColor();
            var layout = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(40) };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
            host.Children.Add(layout);

            // Cover (abgeschrägt, Kante in der Cover-Farbe)
            const double width = 250, height = 360;
            double cut = 26;
            var coverImage = new Border { Width = width, Height = height, Background = MakeBrush("#141C2E"), Clip = ChamferGeometry(width, height, cut) };
            if (HasCover(game) && GetCachedImage(EffectiveCover(game)) is BitmapImage bitmap)
                coverImage.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            var edge = new System.Windows.Shapes.Path { Data = ChamferGeometry(width, height, cut), Stroke = HoloEdgeBrush(0xFF), StrokeThickness = 2 };
            WithCoverColor(game, color =>
            {
                edge.Stroke = new LinearGradientBrush(color, HoloCyan, 45);
                edge.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = color, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.9 };
            });
            var coverGrid = new Grid { Width = width, Height = height };
            coverGrid.Children.Add(coverImage);
            coverGrid.Children.Add(edge);
            var cover = new Border { Child = coverGrid, Margin = new Thickness(0, 0, 44, 0), RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var coverScale = new ScaleTransform(0.82, 0.82);
            cover.RenderTransform = new TransformGroup { Children = { coverScale, new TranslateTransform() } };
            layout.Children.Add(cover);
            launchOverlayCover = cover;

            // Info
            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(info, 1);
            layout.Children.Add(info);

            info.Children.Add(ConsoleEyebrow("// " + Loc.T("Start-Sequenz").ToUpper(Loc.Culture) + "   ·   " + Loc.T(game.Source).ToUpper(Loc.Culture)));
            var title = ConsoleTitle(game.Name, 48);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            var titleHost = new Grid();
            titleHost.Children.Add(title);
            info.Children.Add(titleHost);
            MakeGlitchText(title);

            var facts = new WrapPanel { Margin = new Thickness(0, 16, 0, 22) };
            facts.Children.Add(ConsoleChip(Loc.T("Sitzung"), "#" + Math.Max(1, game.LaunchCount).ToString(Loc.Culture)));
            if (!SHide(settings.StreamHideStats) && game.PlaySeconds >= 60) facts.Children.Add(ConsoleChip(Loc.T("Spielzeit"), FormatPlaytime(game.PlaySeconds)));
            if (!SHide(settings.StreamHideRecent) && settings.GameSessions.TryGetValue(game.Name, out var sessions) && sessions is { Count: > 0 })
                facts.Children.Add(ConsoleChip(Loc.T("Letzte Sitzung"), FormatPlaytime((long)sessions[0].Seconds)));
            info.Children.Add(facts);

            // Systemanzeige: drei Balken aus Segmenten
            var bars = new StackPanel();
            var updates = new List<Action<double>>();
            void Bar(string caption, System.Windows.Media.Color tint)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                var label = HoloLabel(caption, 11);
                label.VerticalAlignment = System.Windows.VerticalAlignment.Center;
                row.Children.Add(label);
                const int segments = 28;
                var cells = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = segments, Height = 12 };
                for (int i = 0; i < segments; i++) cells.Children.Add(new Border { Margin = new Thickness(1.5, 0, 1.5, 0), RenderTransform = new SkewTransform(-20, 0), Background = MakeBrush("#18223A") });
                Grid.SetColumn(cells, 1);
                row.Children.Add(cells);
                var value = new TextBlock { Text = "–", FontSize = 12, Foreground = System.Windows.Media.Brushes.White, TextAlignment = TextAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                value.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(value, 2);
                row.Children.Add(value);
                bars.Children.Add(row);
                var lit = new SolidColorBrush(tint);
                var off = MakeBrush("#18223A");
                updates.Add(percent =>
                {
                    int count = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * segments);
                    for (int i = 0; i < segments; i++) ((Border)cells.Children[i]).Background = i < count ? lit : off;
                    value.Text = percent.ToString("0", Loc.Culture) + " %";
                });
            }
            Bar("CPU", accent);
            Bar("GPU", HoloCyan);
            Bar("RAM", MakeColor("#A78BFA"));
            info.Children.Add(bars);

            var statusRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var spinner = new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = new SolidColorBrush(HoloCyan), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new RotateTransform(45) };
            ((RotateTransform)spinner.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(45, 405, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever });
            launchOverlaySpinner = spinner;
            statusRow.Children.Add(spinner);
            var status = new TextBlock { Text = Loc.T("Spiel wird gestartet ..."), FontSize = 14, Foreground = ConsoleCyan, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            status.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            launchOverlayStatus = status;
            statusRow.Children.Add(status);
            info.Children.Add(statusRow);

            // Einflug: Cover wächst, Info gleitet von rechts
            var ease = new QuinticEase { EasingMode = EasingMode.EaseOut };
            coverScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });
            coverScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(620)) { EasingFunction = ease });
            var slide = new TranslateTransform(40, 0);
            info.RenderTransform = slide;
            info.Opacity = 0;
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(620)) { BeginTime = TimeSpan.FromMilliseconds(120), EasingFunction = ease });
            info.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromMilliseconds(120) });
            Dispatcher.BeginInvoke(new Action(() => PlayCyberScan(host)), DispatcherPriority.Loaded);

            // Werte zweimal pro Sekunde (der Messer fragt höchstens alle 400 ms ab)
            cyberHudTimer?.Stop();
            cyberHudTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            void Tick()
            {
                if (launchOverlay != host) { cyberHudTimer?.Stop(); return; }
                MetricsSample sample;
                try { sample = SampleMetrics(); }
                catch { return; }
                updates[0](sample.Cpu);
                updates[1](sample.HasGpu ? sample.Gpu : 0);
                updates[2](sample.Ram);
            }
            cyberHudTimer.Tick += (s, e) => Tick();
            cyberHudTimer.Start();
            Tick();
        }

        // ───────── Einstellungen: Karte „Effekte von Thema 2“ ─────────

        private void BuildCyberEffectsCard()
        {
            if (!IsBrutal || SettingsCards == null) return;

            var body = new StackPanel();
            var heading = new TextBlock { Text = Loc.T("Effekte von Thema 2") };
            heading.SetResourceReference(StyleProperty, "SectionTitle");
            body.Children.Add(heading);

            void Toggle(string title, string description, Func<bool> get, Action<bool> set)
            {
                var box = new System.Windows.Controls.CheckBox { IsChecked = get(), Margin = new Thickness(0, 0, 0, 4) };
                box.Content = new TextBlock { Text = Loc.T(title), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                box.Checked += (s, e) => { set(true); SaveSettings(); };
                box.Unchecked += (s, e) => { set(false); SaveSettings(); };
                body.Children.Add(box);
                var hint = new TextBlock { Text = Loc.T(description), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
                hint.SetResourceReference(StyleProperty, "SubtleText");
                hint.FontSize = 12;
                body.Children.Add(hint);
            }

            Toggle("Leuchten in der Cover-Farbe", "Beim Hovern über ein Spiel leuchtet die Kante in der Hauptfarbe seines Covers.", () => settings.CyberCoverGlow, v => settings.CyberCoverGlow = v);
            Toggle("Lebender Hintergrund", "Das Punkteraster wandert langsam und folgt leicht der Maus. Im Leistungsmodus aus.", () => settings.CyberLiveBackground, v => { settings.CyberLiveBackground = v; RebuildHoloBackdrops(); });
            Toggle("Seitenwechsel als Scan-Linie", "Beim Wechsel der Seite fährt eine Lichtlinie über den Bildschirm.", () => settings.CyberScanTransition, v => settings.CyberScanTransition = v);
            Toggle("Start-HUD beim Spielstart", "Zeigt beim Start Cover, Sitzungszähler und eine kleine Systemanzeige (CPU, GPU, RAM).", () => settings.CyberLaunchHud, v => settings.CyberLaunchHud = v);
            Toggle("Begrüßung nach Tageszeit", "Status-Zeile wie SYSTEM ONLINE oder NACHTMODUS AKTIV; nachts leuchtet alles etwas gedämpfter.", () => settings.CyberGreeting, v => { settings.CyberGreeting = v; cyberNightActive = !CyberNightNow; CheckCyberNight(); });
            Toggle("Holo-Klänge", "Leise Töne beim Hovern, Öffnen und Starten. Im Streamer-Modus und bei „Nicht stören“ stumm.", () => settings.CyberSounds, v => { settings.CyberSounds = v; if (v) PlayCyberSound("confirm"); });

            // Lautstärke
            var line = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            line.Children.Add(new TextBlock { Text = Loc.T("Lautstärke der Klänge"), Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) });
            var slider = new System.Windows.Controls.Slider { Minimum = 5, Maximum = 100, Value = Math.Max(5, settings.CyberSoundVolume), IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var value = new TextBlock { Text = settings.CyberSoundVolume + " %", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            slider.ValueChanged += (s, e) =>
            {
                settings.CyberSoundVolume = (int)Math.Round(e.NewValue);
                value.Text = settings.CyberSoundVolume + " %";
                SaveSettings();
            };
            slider.PreviewMouseLeftButtonUp += (s, e) => PlayCyberSound("click");
            Grid.SetColumn(slider, 1);
            line.Children.Add(slider);
            Grid.SetColumn(value, 2);
            line.Children.Add(value);
            body.Children.Add(line);

            var card = new Border { Tag = "look", Margin = new Thickness(0, 0, 0, 16), Child = body, Name = "CyberEffectsCard" };
            card.SetResourceReference(StyleProperty, "CardStyle");
            // Direkt hinter die Karte „Farben von Thema 2“
            int index = SettingsCards.Children.OfType<Border>().ToList().FindIndex(b => b.Name == "CyberColorsCard");
            if (index < 0) index = SettingsCards.Children.OfType<Border>().ToList().FindIndex(b => b.Tag as string == "look");
            SettingsCards.Children.Insert(Math.Max(0, index + 1), card);
        }

        /// <summary>Hintergrund der Holo-Ebene neu erzeugen (z. B. nach dem Umschalten des lebenden Hintergrunds).</summary>
        private void RebuildHoloBackdrops()
        {
            if (holoHost != null && holoHost.Children.Count > 0)
            {
                holoHost.Children.RemoveAt(0);
                holoHost.Children.Insert(0, BuildHoloBackdrop());
            }
        }
    }
}
