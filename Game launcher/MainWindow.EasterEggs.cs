using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Interop = System.Runtime.InteropServices;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Über den Launcher ─────────────────────────────

        private void ShowAbout()
        {
            var dialog = CreateDialog("Über DFP Pro Launcher", 460, out var panel);

            panel.Children.Add(new ContentControl
            {
                Content = FindResource("DfpLogo"),
                Width = 110,
                Height = 110,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 14)
            });

            panel.Children.Add(new TextBlock
            {
                Text = "DFP Pro Launcher",
                FontSize = 26,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            var versionLabel = new TextBlock
            {
                Text = "Version " + VersionLabel(),
                Foreground = BrushSubtle,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 18)
            };
            versionLabel.MouseLeftButtonUp += VersionText_Click;
            panel.Children.Add(versionLabel);

            var creator = new TextBlock
            {
                Text = Loc.T("Erstellt von") + " Der_Fette_Pudel",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            creator.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            panel.Children.Add(creator);

            panel.Children.Add(new TextBlock
            {
                Text = "© " + DateTime.Now.Year + " Der_Fette_Pudel",
                Foreground = BrushSubtle,
                FontSize = 12,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 20)
            });

            var close = new System.Windows.Controls.Button
            {
                Content = "Schließen",
                Padding = new Thickness(28, 8, 28, 8),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                IsCancel = true,
                IsDefault = true
            };
            close.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            panel.Children.Add(close);

            dialog.ShowDialog();
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e) => ShowAbout();

        // ═════════════════════════════ Easter Eggs ═════════════════════════════

        private static readonly System.Windows.Input.Key[] KonamiKeys =
        {
            System.Windows.Input.Key.Up, System.Windows.Input.Key.Up, System.Windows.Input.Key.Down, System.Windows.Input.Key.Down,
            System.Windows.Input.Key.Left, System.Windows.Input.Key.Right, System.Windows.Input.Key.Left, System.Windows.Input.Key.Right,
            System.Windows.Input.Key.B, System.Windows.Input.Key.A
        };

        private static readonly PadButton[] KonamiPad =
        {
            PadButton.Up, PadButton.Up, PadButton.Down, PadButton.Down,
            PadButton.Left, PadButton.Right, PadButton.Left, PadButton.Right,
            PadButton.Back, PadButton.Confirm
        };

        private static readonly string[] RareGreetings =
        {
            "Na, wer da?", "Schön, dass du da bist", "Der Pudel hat dich vermisst", "Level up, Legende",
            "Neues Spiel, neues Glück", "Zeit zu zocken", "Willkommen im Hauptquartier", "Bereit für ein Abenteuer"
        };

        private static readonly (int Hours, string Icon, string Title)[] PlaytimeMilestones =
        {
            (100, "🏃", "Couch-Rookie"),
            (500, "🛋", "Couch-Veteran"),
            (1000, "👑", "Pudel-Legende"),
            (2500, "🌌", "Zeitlos"),
            (5000, "♾", "Unsterblich")
        };

        private int konamiIndex;
        private int konamiPadIndex;
        private int logoClicks;
        private DateTime lastLogoClick = DateTime.MinValue;
        private int versionClicks;
        private DateTime lastVersionClick = DateTime.MinValue;
        private int diceSixes;
        private DispatcherTimer? rainbowTimer;
        private DateTime rainbowEnd;
        private double rainbowHue;

        private bool EggsOn => settings.EasterEggs;

        private void InitExtras10()
        {
            if (string.IsNullOrEmpty(settings.FirstRunDate))
            {
                settings.FirstRunDate = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                SaveSettings();
            }

            BrandLogo.Background = System.Windows.Media.Brushes.Transparent;
            BrandLogo.MouseLeftButtonUp += BrandLogo_Click;
            TxtAboutVersion.MouseLeftButtonUp += VersionText_Click;

            Loaded += async (s, e) =>
            {
                await Task.Delay(3800);
                CheckSeasonalEggs();
            };
        }

        // ───────────────────────────── Hilfen ─────────────────────────────

        private void EggToast(string icon, string title, string text, int seconds = 7, bool byUser = true)
            => ShowToast(icon, title, text, seconds, null, byUser);

        private void UnlockSecret(string id)
        {
            if (!EggsOn || settings.Achievements.ContainsKey(id)) return;

            settings.Achievements[id] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            SaveSettings();

            var def = GetAchievementDefs().FirstOrDefault(d => d.Id == id);
            if (def != null)
                ShowToast("🏆", Loc.T("Geheimer Erfolg:") + " " + Loc.T(def.Title), Loc.T(def.Description), 8, () => NavigateTo("stats"), true);

            Confetti();
            if (ViewStats.Visibility == Visibility.Visible) RefreshStatsExtras();
        }

        private bool SeenEgg(string key)
        {
            if (settings.EggSeen.Contains(key)) return true;

            settings.EggSeen.Add(key);
            if (settings.EggSeen.Count > 60) settings.EggSeen.RemoveRange(0, settings.EggSeen.Count - 60);
            SaveSettings();
            return false;
        }

        private static System.Windows.Media.Color HslColor(double hue, double saturation, double lightness)
        {
            double c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
            double m = lightness - c / 2;
            double r, g, b;

            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            return System.Windows.Media.Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
        }

        private static bool IsCreatorName(string name)
            => Regex.Replace((name ?? string.Empty).ToLowerInvariant(), "[^a-z]", string.Empty) == "derfettepudel";

        // ───────────────────────────── Regenbogen und Disco ─────────────────────────────

        private void StartRainbow(int seconds, double step)
        {
            if (settings.PerformanceMode || RgbActive) return;

            rainbowTimer?.Stop();
            rainbowEnd = DateTime.Now.AddSeconds(seconds);
            rainbowTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            rainbowTimer.Tick += (s, e) =>
            {
                if (DateTime.Now >= rainbowEnd)
                {
                    rainbowTimer?.Stop();
                    ApplyTheme();
                    return;
                }

                rainbowHue = (rainbowHue + step) % 360;
                Resources["AccentBrush"] = new SolidColorBrush(HslColor(rainbowHue, 0.85, 0.6));
            };
            rainbowTimer.Start();
        }

        // ───────────────────────────── Konami-Code ─────────────────────────────

        private void TrackKonami(System.Windows.Input.Key key)
        {
            if (!EggsOn || System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            if (key == KonamiKeys[konamiIndex])
            {
                konamiIndex++;
                if (konamiIndex == KonamiKeys.Length)
                {
                    konamiIndex = 0;
                    TriggerKonami();
                }
            }
            else
            {
                konamiIndex = key == KonamiKeys[0] ? 1 : 0;
            }
        }

        private void TrackKonamiPad(PadButton pressed)
        {
            if (!EggsOn || controllerMode || pressed == PadButton.None) return;

            if (pressed == KonamiPad[konamiPadIndex])
            {
                konamiPadIndex++;
                if (konamiPadIndex == KonamiPad.Length)
                {
                    konamiPadIndex = 0;
                    TriggerKonami();
                }
            }
            else
            {
                konamiPadIndex = pressed == KonamiPad[0] ? 1 : 0;
            }
        }

        private void TriggerKonami()
        {
            Confetti();
            Dispatcher.BeginInvoke(new Action(Confetti), DispatcherPriority.Background);
            EggToast("🕹", "+30 Leben", "Konami-Code eingegeben. Der Regenbogen-Modus läuft eine Minute.", 8);
            StartRainbow(60, 5);
            PlayUiSound("select");
            UnlockSecret("secret_konami");
            UnlockRgbWithPopup();
        }

        // ───────────────────────────── Logo anklicken ─────────────────────────────

        private void HopLogo(bool spin)
        {
            if (!settings.PageAnimations) return;

            var group = BrandLogo.RenderTransform as TransformGroup;
            if (group == null)
            {
                group = new TransformGroup();
                group.Children.Add(new RotateTransform(0));
                group.Children.Add(new TranslateTransform(0, 0));
                BrandLogo.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                BrandLogo.RenderTransform = group;
            }

            var rotate = (RotateTransform)group.Children[0];
            var move = (TranslateTransform)group.Children[1];

            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, spin ? -26 : -12, TimeSpan.FromMilliseconds(spin ? 320 : 130))
            {
                AutoReverse = true,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });

            if (spin)
            {
                rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(640))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                });
            }
        }

        private void BrandLogo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!EggsOn) return;

            if ((DateTime.Now - lastLogoClick).TotalSeconds > 2) logoClicks = 0;
            lastLogoClick = DateTime.Now;
            logoClicks++;

            if (logoClicks < 7)
            {
                HopLogo(false);
                return;
            }

            logoClicks = 0;
            PudelGreeting();
        }

        private void PudelGreeting()
        {
            HopLogo(true);
            PlayUiSound("bark");
            EggToast("🐶", "Wuff!", "Der Pudel freut sich, dich zu sehen.", 6);
            UnlockSecret("secret_pudel");
        }

        // ───────────────────────────── Versionsnummer anklicken ─────────────────────────────

        private void VersionText_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!EggsOn) return;

            if ((DateTime.Now - lastVersionClick).TotalSeconds > 3) versionClicks = 0;
            lastVersionClick = DateTime.Now;
            versionClicks++;

            if (versionClicks < 10) return;

            versionClicks = 0;
            ShowSecretStats();
        }

        private void ShowSecretStats()
        {
            double seconds = allGames.Sum(g => (double)g.PlaySeconds);
            long hours = (long)(seconds / 3600);
            long days = hours / 24;
            long films = hours / 2;
            long coffee = hours / 3;
            int launches = allGames.Sum(g => g.LaunchCount);
            var top = allGames.OrderByDescending(g => g.PlaySeconds).FirstOrDefault();

            var dialog = CreateDialog("Geheimstatistik", 520, out var panel);
            panel.Children.Add(new TextBlock { Text = "🔍", FontSize = 40, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Geheimstatistik"),
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            void Line(string text)
                => panel.Children.Add(new TextBlock
                {
                    Text = text,
                    Foreground = MakeBrush("#D1D5DB"),
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                });

            Line(Loc.T($"Du hast insgesamt {hours} Stunden gespielt, das sind {days} Tage am Stück."));
            Line(Loc.T($"In dieser Zeit hättest du {films} Filme schauen können."));
            Line(Loc.T($"Bei einer Tasse alle drei Stunden wären das {coffee} Tassen Kaffee."));
            Line(Loc.T($"Du hast {launches}-mal ein Spiel über den Launcher gestartet."));
            if (top != null && top.PlaySeconds >= 60)
                Line(Loc.T($"Dein Dauerbrenner ist „{top.Name}“ mit {top.PlaySeconds / 3600} Stunden."));

            if (DateTime.TryParse(settings.FirstRunDate, null, DateTimeStyles.RoundtripKind, out var first))
            {
                int daysTogether = Math.Max(0, (int)(DateTime.Now - first).TotalDays);
                Line(Loc.T($"Der Pudel begleitet dich seit {daysTogether} Tagen."));
            }

            var close = new System.Windows.Controls.Button
            {
                Content = Loc.T("Schließen"),
                Padding = new Thickness(28, 8, 28, 8),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                IsCancel = true,
                IsDefault = true
            };
            close.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            panel.Children.Add(close);

            UnlockSecret("secret_stats");
            dialog.ShowDialog();
        }

        // ───────────────────────────── Suchbefehle ─────────────────────────────

        private bool AddEggCommands(string query, List<SpotlightEntry> entries)
        {
            if (!EggsOn || !query.StartsWith("/", StringComparison.Ordinal)) return false;

            string[] parts = query.Substring(1).Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            string token = parts[0];
            string argument = parts.Length > 1 ? parts[1] : string.Empty;

            void Command(string icon, string title, string subtitle, Action action, params string[] names)
            {
                if (names.Any(n => n.StartsWith(token, StringComparison.Ordinal)))
                    entries.Add(new SpotlightEntry { Icon = icon, Title = title, Subtitle = subtitle, Action = action, Score = 100 });
            }

            Command("🎲", "/würfel", "Würfelt eine Zahl (zum Beispiel /würfel 20)", () => RollDice(argument), "würfel", "wuerfel", "dice");
            Command("🪙", "/münze", "Wirft eine Münze", FlipCoin, "münze", "muenze", "coin");
            Command("🎮", "/zufall", "Wählt ein Spiel für dich aus", () => BtnRandomGame_Click(this, new RoutedEventArgs()), "zufall", "random");
            Command("🪩", "/disco", "Startet die Party", StartDisco, "disco", "party");
            Command("🐶", "/pudel", "Sag dem Pudel Hallo", PudelGreeting, "pudel", "wuff", "hund");
            Command("❓", "/hilfe", "Zeigt die geheimen Befehle", ShowEggHelp, "hilfe", "help");

            return entries.Count > 0;
        }

        private void ShowEggHelp()
            => EggToast("❓", "Geheime Befehle", "/würfel  /münze  /zufall  /disco  /pudel", 10);

        private void RollDice(string argument)
        {
            int sides = int.TryParse(argument, out int parsed) ? Math.Clamp(parsed, 2, 1000) : 6;
            int value = random.Next(1, sides + 1);
            PlayUiSound("dice");
            EggToast("🎲", "Würfel", Loc.T($"Du hast eine {value} gewürfelt. ({sides} Seiten)"), 6);

            diceSixes = sides == 6 && value == 6 ? diceSixes + 1 : 0;
            if (diceSixes >= 3)
            {
                diceSixes = 0;
                UnlockSecret("secret_dice");
            }
        }

        private void FlipCoin()
        {
            PlayUiSound("dice");
            EggToast("🪙", "Münze", random.Next(2) == 0 ? Loc.T("Kopf!") : Loc.T("Zahl!"), 5);
        }

        private void StartDisco()
        {
            Confetti();
            StartRainbow(10, 16);
            EggToast("🪩", "Disco", "Die Party läuft für zehn Sekunden.", 6);
            UnlockSecret("secret_disco");
        }

        // ───────────────────────────── Besondere Tage ─────────────────────────────

        private void EmojiRain(string[] glyphs, int count, int milliseconds)
        {
            if (!settings.PageAnimations || ConfettiLayer == null) return;

            double width = ConfettiLayer.ActualWidth > 0 ? ConfettiLayer.ActualWidth : ActualWidth;
            double height = ConfettiLayer.ActualHeight > 0 ? ConfettiLayer.ActualHeight : ActualHeight;

            for (int i = 0; i < count; i++)
            {
                var flake = new TextBlock
                {
                    Text = glyphs[random.Next(glyphs.Length)],
                    FontSize = random.Next(16, 34),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                    Opacity = 0.9
                };

                double startX = random.NextDouble() * width;
                Canvas.SetLeft(flake, startX);
                Canvas.SetTop(flake, -40);
                ConfettiLayer.Children.Add(flake);

                var duration = TimeSpan.FromMilliseconds(milliseconds * (0.55 + random.NextDouble() * 0.45));
                var delay = TimeSpan.FromMilliseconds(random.Next(0, milliseconds / 2));

                var fall = new DoubleAnimation(-40, height + 50, duration) { BeginTime = delay };
                fall.Completed += (s, e) => ConfettiLayer.Children.Remove(flake);

                flake.BeginAnimation(Canvas.TopProperty, fall);
                flake.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(startX, startX + random.Next(-70, 70), duration) { BeginTime = delay });
            }
        }

        private void Fireworks(int bursts)
        {
            if (!settings.PageAnimations || ConfettiLayer == null) return;

            double width = ConfettiLayer.ActualWidth > 0 ? ConfettiLayer.ActualWidth : ActualWidth;
            double height = ConfettiLayer.ActualHeight > 0 ? ConfettiLayer.ActualHeight : ActualHeight;
            string[] colors = { "#FBBF24", "#F472B6", "#60A5FA", "#34D399", "#F87171", "#A78BFA" };

            for (int burst = 0; burst < bursts; burst++)
            {
                double centerX = width * (0.15 + 0.7 * random.NextDouble());
                double centerY = height * (0.15 + 0.45 * random.NextDouble());
                var brush = MakeBrush(colors[random.Next(colors.Length)]);
                var delay = TimeSpan.FromMilliseconds(burst * 480);
                const int rays = 28;

                for (int ray = 0; ray < rays; ray++)
                {
                    double angle = Math.PI * 2 * ray / rays;
                    double distance = 80 + random.Next(0, 90);
                    var spark = new System.Windows.Shapes.Ellipse { Width = 6, Height = 6, Fill = brush };

                    Canvas.SetLeft(spark, centerX);
                    Canvas.SetTop(spark, centerY);
                    ConfettiLayer.Children.Add(spark);

                    var time = TimeSpan.FromMilliseconds(1200);
                    var fade = new DoubleAnimation(1, 0, time) { BeginTime = delay };
                    fade.Completed += (s, e) => ConfettiLayer.Children.Remove(spark);

                    spark.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(centerX, centerX + Math.Cos(angle) * distance, time) { BeginTime = delay });
                    spark.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(centerY, centerY + Math.Sin(angle) * distance + 36, time) { BeginTime = delay });
                    spark.BeginAnimation(UIElement.OpacityProperty, fade);
                }
            }
        }

        private void CheckSeasonalEggs()
        {
            if (!EggsOn || activeSessions.Count > 0 || WelcomeLayer.Visibility == Visibility.Visible) return;

            var now = DateTime.Now;
            bool celebrated = false;

            if (now.Month == 10 && now.Day == 31 && !SeenEgg($"halloween-{now.Year}"))
            {
                PlayHalloween();
                celebrated = true;
            }
            else if (now.Month == 12 && now.Day is >= 24 and <= 26 && !SeenEgg($"xmas-{now.Year}"))
            {
                PlayChristmas();
                celebrated = true;
            }
            else if (((now.Month == 12 && now.Day == 31 && now.Hour >= 17) || (now.Month == 1 && now.Day == 1)) && !SeenEgg($"newyear-{now.Year}"))
            {
                PlayNewYear();
                celebrated = true;
            }

            if (celebrated) UnlockSecret("secret_season");

            if (now.Month == 4 && now.Day == 1 && !StreamerOn && !SeenEgg($"april-{now.Year}"))
            {
                ShowAprilFools();
                UnlockSecret("secret_season");
            }

            if (DateTime.TryParse(settings.FirstRunDate, null, DateTimeStyles.RoundtripKind, out var first)
                && now.Year > first.Year && now.Month == first.Month && now.Day == first.Day
                && !SeenEgg($"birthday-{now.Year}"))
            {
                PlayBirthday(now.Year - first.Year);
            }
        }

        private void ShowAprilFools()
        {
            var dialog = CreateDialog("DFP Pro Launcher", 540, out var panel);
            dialog.Topmost = true;

            var icon = new TextBlock { Text = "⚠", FontSize = 44, Foreground = MakeBrush("#FBBF24"), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            var title = new TextBlock
            {
                Text = Loc.T("DFP Pro Launcher funktioniert nicht mehr"),
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 8)
            };
            var message = new TextBlock
            {
                Text = Loc.T("Ein Problem hat dazu geführt, dass das Programm nicht mehr richtig funktioniert. Windows sucht nach einer Lösung für das Problem ..."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            panel.Children.Add(icon);
            panel.Children.Add(title);
            panel.Children.Add(message);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var first = new System.Windows.Controls.Button { Content = Loc.T("Programm schließen"), Margin = new Thickness(0, 0, 10, 0) };
            var second = new System.Windows.Controls.Button { Content = Loc.T("Auf Lösung warten"), Padding = new Thickness(20, 8, 20, 8) };
            second.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(first);
            buttons.Children.Add(second);
            panel.Children.Add(buttons);

            bool revealed = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };

            void Reveal()
            {
                if (revealed) return;
                revealed = true;
                timer.Stop();

                icon.Text = "🐶";
                title.Text = Loc.T("April, April!");
                message.Text = Loc.T("Alles ist in Ordnung. Der Pudel hat dich nur kurz erschreckt.");
                first.Visibility = Visibility.Collapsed;
                second.Content = Loc.T("Puh, danke!");
                second.IsDefault = true;
                Confetti();
            }

            timer.Tick += (s, e) => Reveal();
            first.Click += (s, e) => Reveal();
            second.Click += (s, e) =>
            {
                if (!revealed) Reveal();
                else dialog.DialogResult = true;
            };
            dialog.Closed += (s, e) => timer.Stop();

            timer.Start();
            dialog.ShowDialog();
        }

        // ───────────────────────────── Nachteule, Begrüßung, Meilensteine ─────────────────────────────

        private void CheckNightEgg(DateTime now)
        {
            if (!EggsOn || activeSessions.Count == 0 || now.Hour < 3 || now.Hour >= 5) return;
            if (SeenEgg($"owl-{now:yyyyMMdd}")) return;

            PlayNightOwl();
        }

        private string? PickGreeting(out bool rare)
        {
            rare = false;
            if (!EggsOn) return null;

            if (random.Next(50) == 0)
            {
                rare = true;
                return Loc.T(RareGreetings[random.Next(RareGreetings.Length)]);
            }
            return null;
        }

        private void CheckMilestones()
        {
            double hours = allGames.Sum(g => (double)g.PlaySeconds) / 3600.0;
            bool baseline = !settings.MilestonesInit;
            settings.MilestonesInit = true;

            foreach (var milestone in PlaytimeMilestones)
            {
                if (hours < milestone.Hours || settings.MilestonesReached.Contains(milestone.Hours)) continue;

                settings.MilestonesReached.Add(milestone.Hours);
                if (baseline || !EggsOn) continue;

                ShowToast(milestone.Icon, Loc.T("Meilenstein") + ": " + Loc.T(milestone.Title),
                    Loc.T($"Du hast insgesamt {milestone.Hours} Stunden gespielt."), 10, null, true);
                Confetti();
            }

            SaveSettings();
        }

        // ───────────────────────────── Einstellungen ─────────────────────────────

        private void ReadExtra10Settings() => settings.EasterEggs = ChkEggs.IsChecked == true;

        private void PopulateExtra10Settings() => ChkEggs.IsChecked = settings.EasterEggs;

        private IEnumerable<AchievementDef> SecretAchievementDefs()
        {
            AchievementDef Secret(string id, string icon, string title, string description)
                => new()
                {
                    Id = id,
                    Icon = icon,
                    Title = title,
                    Description = description,
                    Current = settings.Achievements.ContainsKey(id) ? 1 : 0,
                    Target = 1,
                    Secret = true
                };

            yield return Secret("secret_konami", "🕹", "Konami-Code", "Du kennst die alten Cheats.");
            yield return Secret("secret_pudel", "🐶", "Pudel-Freund", "Du hast dem Pudel Hallo gesagt.");
            yield return Secret("secret_dice", "🎲", "Würfelglück", "Drei Sechsen in Folge gewürfelt.");
            yield return Secret("secret_disco", "🪩", "Disco-Fieber", "Die Party gestartet.");
            yield return Secret("secret_grape", "🍇", "Trauben-Freundschaft", "Du hast die Hommage an TraubeMinze gefunden.");
            yield return Secret("secret_sky", "☁", "Wolkenhirte", "Du hast die clouds von StardiSkyTTV gefunden.");
            yield return Secret("secret_creator", "👑", "Der Creator", "Du hast den Creator-Namen eingetragen.");
            yield return Secret("secret_owl", "🦉", "Nachteule", "Nach drei Uhr nachts gespielt.");
            yield return Secret("secret_birthday", "🎂", "Geburtstagskind", "Am Jahrestag der Installation gestartet.");
            yield return Secret("secret_stats", "📊", "Zahlenmensch", "Die Geheimstatistik gefunden.");
            yield return Secret("secret_lucky", "🍀", "Glückspilz", "Einen seltenen Spruch beim Start bekommen.");
            yield return Secret("secret_season", "🎉", "Festtagsstimmung", "Einen besonderen Tag im Launcher gefeiert.");
        }

        // ───────────────────────────── Namens-Easter-Eggs ─────────────────────────────


        private void PlayCreatorEgg()
        {
            EmojiRain(new[] { "👑", "🐩", "✨" }, 38, 6500);
            Confetti();
            EggToast("👑", "Der Creator persönlich", "Willkommen zurück, Chef!", 8);
            UnlockSecret("secret_creator");
        }

        // ───────────────────────────── Namens-Easter-Eggs über Strg + K ─────────────────────────────

        private sealed class EggRun
        {
            public string Kind = string.Empty;
            public Canvas Layer = null!;
            public DispatcherTimer? Frame;
            public DispatcherTimer? Hop;
            public DispatcherTimer? End;
        }

        private readonly List<EggRun> eggRuns = new();

        /// <summary>Die Namen sind geheim: Sie erscheinen nie als Vorschlag und lösen nur aus, wenn sie in der Suche eingegeben werden.</summary>
        private bool TryPlayEggFromSearch(string text)
        {
            if (!EggsOn) return false;

            switch (Regex.Replace(text.ToLowerInvariant(), "[^a-z]", string.Empty))
            {
                case "traubeminze":
                    PlayTraubeMinze();
                    return true;
                case "stardiskyttv":
                    PlayStardiSky();
                    return true;
                default:
                    return false;
            }
        }

        private bool CheckNameEgg(string name)
        {
            if (!EggsOn) return false;
            if (Regex.Replace(name.ToLowerInvariant(), "[^a-z]", string.Empty) != "derfettepudel") return false;

            PlayCreatorEgg();
            return true;
        }

        private EggRun BeginEggRun(string kind)
        {
            // Dieselbe Animation startet neu, andere laufen parallel weiter
            foreach (var old in eggRuns.Where(r => r.Kind == kind).ToList()) EndEggRun(old, true);

            var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
            System.Windows.Controls.Panel.SetZIndex(layer, 52);
            WindowRoot.Children.Add(layer);

            var run = new EggRun { Kind = kind, Layer = layer };
            eggRuns.Add(run);
            return run;
        }

        private void EndEggRun(EggRun run, bool immediately)
        {
            run.Frame?.Stop();
            run.Hop?.Stop();
            run.End?.Stop();
            eggRuns.Remove(run);

            if (immediately)
            {
                run.Layer.BeginAnimation(UIElement.OpacityProperty, null);
                WindowRoot.Children.Remove(run.Layer);
                return;
            }

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700));
            fade.Completed += (_, _) => WindowRoot.Children.Remove(run.Layer);
            run.Layer.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void ScheduleEggEnd(EggRun run, int milliseconds)
        {
            run.End = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            run.End.Tick += (s, e) => EndEggRun(run, false);
            run.End.Start();
        }


        private void PlayStardiSky()
        {
            EggToast("☁", "StardiSkyTTV", "Der Himmel gehört dir. Die clouds folgen dir.", 8);
            UnlockSecret("secret_sky");
            if (!settings.PageAnimations) return;

            var run = BeginEggRun("himmel");
            var layer = run.Layer;

            double width = Math.Max(600, WindowRoot.ActualWidth);
            double height = Math.Max(400, WindowRoot.ActualHeight);

            var sky = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Opacity = 0,
                Fill = new LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(0x4F, 0xA8, 0xF5),
                    System.Windows.Media.Color.FromRgb(0xCF, 0xEC, 0xFF),
                    90)
            };
            layer.Children.Add(sky);
            sky.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(1100)));

            var sun = new TextBlock { Text = "☀", FontSize = 110, Foreground = MakeBrush("#FDE047"), Opacity = 0 };
            Canvas.SetLeft(sun, width - 190);
            Canvas.SetTop(sun, 50);
            layer.Children.Add(sun);
            sun.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(1400)));

            var clouds = new List<TextBlock>();
            var positions = new List<System.Windows.Point>();
            for (int i = 0; i < 6; i++)
            {
                var cloud = new TextBlock
                {
                    Text = "☁",
                    FontSize = 64 - i * 7,
                    Foreground = System.Windows.Media.Brushes.White,
                    Opacity = 0.96
                };
                var start = new System.Windows.Point(-120 - i * 90, 120 + i * 40);
                Canvas.SetLeft(cloud, start.X);
                Canvas.SetTop(cloud, start.Y);
                layer.Children.Add(cloud);
                clouds.Add(cloud);
                positions.Add(start);
            }

            var label = new TextBlock
            {
                Text = "clouds",
                FontSize = 15,
                FontStyle = FontStyles.Italic,
                FontWeight = FontWeights.SemiBold,
                Foreground = MakeBrush("#1E3A8A")
            };
            layer.Children.Add(label);

            DateTime started = DateTime.Now;
            run.Frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
            run.Frame.Tick += (s, e) =>
            {
                var mouse = System.Windows.Input.Mouse.GetPosition(layer);
                if (double.IsNaN(mouse.X) || mouse.X < -2000 || mouse.Y < -2000) mouse = new System.Windows.Point(width / 2, height / 2);

                double seconds = (DateTime.Now - started).TotalSeconds;
                double intro = Math.Clamp(seconds / 2.0, 0, 1);
                var target = new System.Windows.Point(mouse.X * intro + width * 0.2 * (1 - intro), mouse.Y * intro + height * 0.3 * (1 - intro));

                for (int i = 0; i < clouds.Count; i++)
                {
                    double follow = 0.085 - i * 0.011;
                    double wobble = Math.Sin(seconds * 2 + i) * 6;
                    var p = positions[i];
                    p.X += (target.X - 40 - i * 34 - p.X) * follow;
                    p.Y += (target.Y + 16 + i * 14 + wobble - p.Y) * follow;
                    positions[i] = p;
                    Canvas.SetLeft(clouds[i], p.X);
                    Canvas.SetTop(clouds[i], p.Y);
                }

                Canvas.SetLeft(label, positions[0].X + 14);
                Canvas.SetTop(label, positions[0].Y + 62);
            };
            run.Frame.Start();

            ScheduleEggEnd(run, 13000);
        }

        // ───────────────────────────── TraubeMinze: Name unter dem jeweiligen Logo ─────────────────────────────

        private void PlayTraubeMinze()
        {
            EggToast("🍇", "TraubeMinze", "Eine Hommage: zwei Launcher, zeitgleich entstanden.", 8);
            UnlockSecret("secret_grape");
            if (!settings.PageAnimations) return;

            var run = BeginEggRun("traube");
            var layer = run.Layer;

            double width = Math.Max(600, WindowRoot.ActualWidth);
            double height = Math.Max(400, WindowRoot.ActualHeight);
            double top = height / 2 - 150;
            double centerX = width / 2;

            const double logoSize = 170;
            double poodleLeft = centerX - logoSize - 70;
            double grapeLeft = centerX + 70;

            var poodle = (FrameworkElement)FindResource("DfpLogo");
            poodle.Width = logoSize;
            poodle.Height = logoSize;
            Canvas.SetTop(poodle, top);
            Canvas.SetLeft(poodle, -260);
            layer.Children.Add(poodle);

            var grape = new TextBlock
            {
                Text = "🍇",
                FontSize = 130,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"),
                Foreground = System.Windows.Media.Brushes.White
            };
            grape.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double grapeWidth = Math.Max(logoSize * 0.8, grape.DesiredSize.Width);
            Canvas.SetTop(grape, top - 8);
            Canvas.SetLeft(grape, width + 260);
            layer.Children.Add(grape);

            TextBlock Caption(string text, double size, FontWeight weight, System.Windows.Media.Brush brush, double centerOfX, double y)
            {
                var block = new TextBlock
                {
                    Text = text,
                    FontSize = size,
                    FontWeight = weight,
                    Foreground = brush,
                    Opacity = 0,
                    Effect = (System.Windows.Media.Effects.Effect)FindResource("TextShadow")
                };
                block.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(block, centerOfX - block.DesiredSize.Width / 2);
                Canvas.SetTop(block, y);
                layer.Children.Add(block);
                return block;
            }

            double nameY = top + logoSize + 12;
            var poodleName = Caption("DFP Pro", 28, FontWeights.Bold, System.Windows.Media.Brushes.White, poodleLeft + logoSize / 2, nameY);
            var grapeName = Caption("TraubeMinze", 28, FontWeights.Bold, System.Windows.Media.Brushes.White, grapeLeft + grapeWidth / 2, nameY);
            var cross = Caption("×", 54, FontWeights.Bold, MakeBrush("#E5E7EB"), centerX, top + logoSize / 2 - 38);
            var subtitle = Caption(Loc.T("Zwei Launcher, eine Idee"), 18, FontWeights.Normal, MakeBrush("#E5E7EB"), centerX, nameY + 52);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slow = TimeSpan.FromMilliseconds(1300);
            poodle.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(-260, poodleLeft, slow) { EasingFunction = ease });
            grape.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(width + 260, grapeLeft, slow) { EasingFunction = ease });

            run.Hop = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1350) };
            run.Hop.Tick += (s, e) =>
            {
                run.Hop?.Stop();

                var bounce = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                poodle.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top, top - 36, TimeSpan.FromMilliseconds(260))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(3),
                    EasingFunction = bounce
                });
                grape.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(top - 8, top - 44, TimeSpan.FromMilliseconds(260))
                {
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(3),
                    BeginTime = TimeSpan.FromMilliseconds(120),
                    EasingFunction = bounce
                });

                var fadeIn = TimeSpan.FromMilliseconds(450);
                poodleName.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeIn));
                grapeName.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeIn) { BeginTime = TimeSpan.FromMilliseconds(150) });
                cross.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeIn) { BeginTime = TimeSpan.FromMilliseconds(300) });
                subtitle.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, fadeIn) { BeginTime = TimeSpan.FromMilliseconds(500) });

                EmojiRain(new[] { "💜", "🍇", "🐩", "✨" }, 34, 4500);
                Confetti();
            };
            run.Hop.Start();

            ScheduleEggEnd(run, 7600);
        }
    }
}
