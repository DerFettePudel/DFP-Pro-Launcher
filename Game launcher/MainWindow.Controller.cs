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
        // ───────────────────────────── Tastatur & Controller-Modus ─────────────────────────────

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            TrackKonami(e.Key);

            if (PadKeyDown(e) || FlowKeyDown(e))
            {
                e.Handled = true;
                return;
            }

            bool ctrl = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0;

            if (ctrl && e.Key == System.Windows.Input.Key.K)
            {
                OpenSpotlight();
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.F11)
            {
                ToggleControllerMode();
                e.Handled = true;
                return;
            }

            if (HandleShortcuts(e, ctrl)) return;

            if (SpotlightLayer.Visibility == Visibility.Visible || !controllerMode) return;
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

            switch (e.Key)
            {
                case System.Windows.Input.Key.Left: MoveControllerSelection(-1, 0); e.Handled = true; break;
                case System.Windows.Input.Key.Right: MoveControllerSelection(1, 0); e.Handled = true; break;
                case System.Windows.Input.Key.Up: MoveControllerSelection(0, -1); e.Handled = true; break;
                case System.Windows.Input.Key.Down: MoveControllerSelection(0, 1); e.Handled = true; break;
                case System.Windows.Input.Key.Enter: LaunchControllerSelection(); e.Handled = true; break;
                case System.Windows.Input.Key.Escape: ToggleControllerMode(); e.Handled = true; break;
            }
        }


        private void HighlightControllerSelection()
        {
            var cards = GamesContainer.Children.OfType<Border>().ToList();
            if (cards.Count == 0) return;

            controllerIndex = Math.Clamp(controllerIndex, 0, cards.Count - 1);

            for (int i = 0; i < cards.Count; i++)
            {
                bool selected = controllerMode && i == controllerIndex;

                if (cards[i].Child is Border inner)
                {
                    inner.BorderThickness = selected ? new Thickness(3) : new Thickness(0);
                    inner.BorderBrush = selected ? (Resources["AccentBrush"] as System.Windows.Media.Brush) : null;
                }

                cards[i].Effect = selected
                    ? new System.Windows.Media.Effects.DropShadowEffect { Color = GetAccentColor(), BlurRadius = 34, ShadowDepth = 0, Opacity = 0.85 }
                    : null;
            }

            cards[controllerIndex].BringIntoView();
        }

        private void MoveControllerSelection(int dx, int dy)
        {
            int count = GamesContainer.Children.OfType<Border>().Count();
            if (count == 0) return;

            double cardWidth = Math.Max(settings.CardWidth, 240) + 24;
            int columns = Math.Max(1, (int)Math.Floor(GamesScroll.ViewportWidth / cardWidth));

            int next = controllerIndex + dx + dy * columns;
            int before = controllerIndex;
            controllerIndex = Math.Clamp(next, 0, count - 1);
            if (controllerIndex != before) PlayUiSound("move");
            HighlightControllerSelection();
        }

        private void LaunchControllerSelection()
        {
            if (controllerIndex >= 0 && controllerIndex < visibleGames.Count)
                LaunchGame(visibleGames[controllerIndex]);
        }

        // ───────────────────────────── Controller: Xbox, PlayStation, Switch und generische Gamepads ─────────────────────────────



        private void UpdatePadHint()
        {
            if (PadHintBar == null) return;

            // Die alte Leiste unten dient nur noch der Akku-Vorschau, die eigentlichen Hinweise stehen im Controller-Modus selbst
            PadHintBar.Visibility = padBatteryPreview ? Visibility.Visible : Visibility.Collapsed;
            if (controllerMode && padLayer != null) RefreshPadHints();
            UpdatePadBatteryUi();
        }

        private string DescribeJoystick(uint id)
        {
            padLayoutDetected = "xbox";

            try
            {
                var caps = new NativePad.JoyCaps();
                if (NativePad.joyGetDevCapsW(id, ref caps, (uint)Interop.Marshal.SizeOf<NativePad.JoyCaps>()) == 0)
                {
                    string name = caps.szPname ?? string.Empty;

                    if (caps.wMid == 0x054C || Regex.IsMatch(name, "Wireless Controller|DualSense|DualShock|PLAYSTATION", RegexOptions.IgnoreCase))
                        padLayoutDetected = "ps";
                    else if (caps.wMid == 0x057E || Regex.IsMatch(name, "Pro Controller|Switch|Joy-Con|Nintendo", RegexOptions.IgnoreCase))
                        padLayoutDetected = "nintendo";

                    string kind = padLayoutDetected == "ps" ? "PlayStation-Controller"
                                : padLayoutDetected == "nintendo" ? "Nintendo-Controller"
                                : "Gamepad";
                    return string.IsNullOrWhiteSpace(name) ? kind : $"{kind} ({name})";
                }
            }
            catch { }

            return "Gamepad";
        }

        private string ResolveLayout()
            => settings.ControllerLayout is "xbox" or "ps" or "nintendo" ? settings.ControllerLayout : padLayoutDetected;

        private PadButton MapJoystick(NativePad.JoyInfoEx info)
        {
            var pad = PadButton.None;
            uint buttons = info.dwButtons;
            bool Has(int bit) => (buttons & (1u << bit)) != 0;

            int confirm, back, details, favorite, start;
            switch (ResolveLayout())
            {
                case "ps":
                    confirm = 1; back = 2; details = 0; favorite = 3; start = 9;      // Kreuz, Kreis, Quadrat, Dreieck, Options
                    break;
                case "nintendo":
                    confirm = 1; back = 0; details = 3; favorite = 2; start = 9;
                    break;
                default:
                    confirm = 0; back = 1; details = 2; favorite = 3; start = 7;
                    break;
            }

            if (Has(confirm)) pad |= PadButton.Confirm;
            if (Has(back)) pad |= PadButton.Back;
            if (Has(details)) pad |= PadButton.Details;
            if (Has(favorite)) pad |= PadButton.Favorite;
            if (Has(4)) pad |= PadButton.PrevTab;
            if (Has(5)) pad |= PadButton.NextTab;
            if (Has(start)) pad |= PadButton.Start;
            if (Has(ResolveLayout() == "xbox" ? 6 : 8)) pad |= PadButton.Select;
            if (ResolveLayout() != "xbox")
            {
                // PlayStation und Nintendo melden L2/R2 (ZL/ZR) als eigene Tasten 6 und 7
                if (Has(6)) pad |= PadButton.TriggerLeft;
                if (Has(7)) pad |= PadButton.TriggerRight;
            }

            // Steuerkreuz (Hat-Schalter, Angabe in Hundertstel Grad)
            if (info.dwPOV < 36000)
            {
                int angle = (int)(info.dwPOV / 100);
                if (angle >= 315 || angle <= 45) pad |= PadButton.Up;
                else if (angle <= 135) pad |= PadButton.Right;
                else if (angle <= 225) pad |= PadButton.Down;
                else pad |= PadButton.Left;
            }

            // Linker Stick
            if (info.dwXpos < 12000) pad |= PadButton.Left;
            if (info.dwXpos > 53000) pad |= PadButton.Right;
            if (info.dwYpos < 12000) pad |= PadButton.Up;
            if (info.dwYpos > 53000) pad |= PadButton.Down;

            return pad;
        }

        private void UpdatePadStatus()
        {
            if (TxtPadStatus == null) return;

            string text = padDescription.Length > 0
                ? Loc.T("Erkannt:") + " " + padDescription
                : Loc.T("Kein Controller erkannt");
            if (padDescription.Length > 0 && padBatteryLevel >= 0) text += " · " + Loc.T("Akku") + ": " + PadBatteryLabel();
            if (TxtPadStatus.Text != text) TxtPadStatus.Text = text;
        }

        // ───────────────────────────── Tasten-Hinweise im Controller-Modus ─────────────────────────────

        // ───────────────────────────── Controller-Akku ─────────────────────────────

        private int padSlot = -1;
        private int padBatteryLevel = -1;        // -1 = unbekannt, 0 leer, 1 niedrig, 2 mittel, 3 voll
        private bool padBatteryWired;
        private bool padBatteryPreview;
        private DateTime lastBatteryRead = DateTime.MinValue;

        private void TickPadBattery()
        {
            if (padBatteryPreview) return;

            if (padSlot < 0)
            {
                if (padBatteryLevel != -1) SetPadBattery(-1, false);
                return;
            }

            if ((DateTime.Now - lastBatteryRead).TotalSeconds < 15) return;
            lastBatteryRead = DateTime.Now;

            try
            {
                if (NativeFeatures.XInputGetBatteryInformation((uint)padSlot, 0, out var info) != 0)
                {
                    SetPadBattery(-1, false);
                    return;
                }

                if (info.BatteryType == 1) SetPadBattery(3, true);                                // Kabel
                else if (info.BatteryType == 2 || info.BatteryType == 3) SetPadBattery(info.BatteryLevel, false);
                else SetPadBattery(-1, false);
            }
            catch
            {
                SetPadBattery(-1, false);
            }
        }

        private void SetPadBattery(int level, bool wired)
        {
            bool wasLow = !padBatteryWired && (padBatteryLevel == 0 || padBatteryLevel == 1);
            padBatteryLevel = level;
            padBatteryWired = wired;
            if (!wired && level >= 0) padLastWirelessLevel = level;

            UpdatePadBatteryUi();
            UpdatePadStatus();

            bool isLow = !wired && (level == 0 || level == 1);
            if (isLow && !wasLow && controllerMode && !padBatteryPreview)
                ShowToast("🎮", "Controller-Akku niedrig", "Der Akku deines Controllers ist fast leer. Zeit zum Aufladen.", 8, null, true);
        }

        private string PadBatteryLabel()
        {
            if (padBatteryLevel < 0) return string.Empty;
            if (padBatteryWired)
                return "🔌 " + Loc.T("Kabelgebunden an") + (padLastWirelessLevel >= 0 ? "  ·  " + BatteryText(padLastWirelessLevel) : string.Empty);

            return padBatteryLevel switch
            {
                3 => "🔋 " + Loc.T("Voll"),
                2 => "🔋 " + Loc.T("Mittel"),
                1 => "🪫 " + Loc.T("Niedrig"),
                _ => "🪫 " + Loc.T("Leer")
            };
        }

        private void UpdatePadBatteryUi()
        {
            UpdatePadPill();
            if (PadBatteryBadge == null) return;

            bool show = padBatteryLevel >= 0 && (controllerMode || padBatteryPreview);
            PadBatteryBadge.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;

            TxtPadBattery.Text = Loc.T("Akku") + ": " + PadBatteryLabel();
            TxtPadBattery.Foreground = MakeBrush(padBatteryWired ? "#60A5FA" : padBatteryLevel switch
            {
                3 => "#34D399",
                2 => "#A3E635",
                1 => "#F59E0B",
                _ => "#EF4444"
            });
        }

        private void PreviewPadBattery(int level, bool wired)
        {
            padBatteryPreview = true;
            padBatteryLevel = level;
            padBatteryWired = wired;

            PadHintBar.Visibility = Visibility.Visible;
            if (!controllerMode) TxtPadHint.Text = Loc.T("Vorschau");
            UpdatePadBatteryUi();

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                padBatteryPreview = false;
                padBatteryLevel = -1;
                UpdatePadBatteryUi();
                UpdatePadHint();
            };
            timer.Start();
        }

        // ───────────────────────────── Controller-Modus: eigene Vollbild-Oberfläche ─────────────────────────────

        private Grid? padLayer;
        private CoverFlowControl? padFlow;
        private TextBlock? padTitle, padMeta, padClockText, padBatteryText, padEmpty, padMenuTitle;
        private StackPanel? padTabs, padHints, padMenuList;
        private Border? padBatteryPill;
        private Grid? padMenuHost;
        private System.Windows.Controls.Image? padBackdrop;
        private DispatcherTimer? padClockTimer;
        private DispatcherTimer? padRumbleTimer;
        private List<PadItem> padItems = new();
        private int padCategory;
        private bool padMenuOpen;
        private int padMenuIndex;
        private List<(string Label, Action Run)> padMenuEntries = new();
        private bool padResumeAfterGame;
        private bool padWasConnected;
        private DateTime? padStartHeld;
        private DateTime padRepeatSince = DateTime.MinValue;
        private DateTime padRepeatLast = DateTime.MinValue;

        private static readonly string[] PadCategories = { "Bibliothek", "Zuletzt gespielt", "Favoriten", "Neu", "Anwendungen" };

        private void EnsurePadLayer()
        {
            if (padLayer != null) return;

            padLayer = new Grid { Visibility = Visibility.Collapsed, Background = MakeBrush("#080A10") };
            System.Windows.Controls.Panel.SetZIndex(padLayer, 150);

            padBackdrop = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
            RenderOptions.SetBitmapScalingMode(padBackdrop, BitmapScalingMode.LowQuality);
            padLayer.Children.Add(padBackdrop);

            var shade = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x08, 0x0A, 0x10), 0.0),
                    new GradientStop(System.Windows.Media.Color.FromArgb(0x99, 0x08, 0x0A, 0x10), 0.45),
                    new GradientStop(System.Windows.Media.Color.FromArgb(0xF2, 0x08, 0x0A, 0x10), 1.0)
                }, 90);
            padLayer.Children.Add(new Border { Background = shade });

            var layout = new Grid { Margin = new Thickness(60, 36, 60, 34) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            padLayer.Children.Add(layout);

            // Kopfzeile: Logo, Bereiche, Uhr und Akku
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var brand = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            var logo = (FrameworkElement)FindResource("DfpLogo");
            logo.Width = 46;
            logo.Height = 46;
            brand.Children.Add(logo);
            brand.Children.Add(new TextBlock
            {
                Text = "DFP PRO",
                FontSize = 22,
                FontWeight = FontWeights.ExtraBold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            top.Children.Add(brand);

            padTabs = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            Grid.SetColumn(padTabs, 1);
            top.Children.Add(padTabs);

            var status = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            padBatteryText = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White };
            padBatteryPill = new Border
            {
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(13, 5, 13, 5),
                CornerRadius = new CornerRadius(15),
                Background = MakeBrush("#26FFFFFF"),
                Margin = new Thickness(0, 0, 20, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = padBatteryText
            };
            padClockText = new TextBlock { FontSize = 26, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            status.Children.Add(padBatteryPill);
            status.Children.Add(padClockText);
            Grid.SetColumn(status, 2);
            top.Children.Add(status);
            layout.Children.Add(top);

            // Mitte: Cover-Flow
            padFlow = new CoverFlowControl { ItemWidth = 250, ItemHeight = 375, Margin = new Thickness(0, 8, 0, 8) };
            padFlow.ItemFactory = index => CreateFlowCard(padItems[index], padFlow.ItemWidth, padFlow.ItemHeight);
            padFlow.FocusChanged = FlowFocus;
            padFlow.SelectionChanged += (s, e) => OnPadSelectionChanged();
            padFlow.ItemActivated += (s, index) => LaunchPadItem();
            padFlow.SizeChanged += (s, e) =>
            {
                double height = Math.Clamp(padFlow.ActualHeight * 0.80, 240, 560);
                if (Math.Abs(padFlow.ItemHeight - height) < 2) return;
                padFlow.ItemHeight = height;
                padFlow.ItemWidth = Math.Round(height * 2.0 / 3.0);
                padFlow.SetCount(padItems.Count, Math.Max(0, padFlow.SelectedIndex));
            };
            Grid.SetRow(padFlow, 1);
            layout.Children.Add(padFlow);

            padEmpty = new TextBlock
            {
                Text = Loc.T("Hier ist noch nichts."),
                FontSize = 22,
                Foreground = MakeBrush("#9CA3AF"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(padEmpty, 1);
            layout.Children.Add(padEmpty);

            // Titel und Angaben
            var info = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 20) };
            padTitle = new TextBlock
            {
                FontSize = 38,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 1300
            };
            padMeta = new TextBlock { FontSize = 18, Foreground = MakeBrush("#B4BCCB"), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            info.Children.Add(padTitle);
            info.Children.Add(padMeta);
            Grid.SetRow(info, 2);
            layout.Children.Add(info);

            // Tastenhinweise
            padHints = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            Grid.SetRow(padHints, 3);
            layout.Children.Add(padHints);

            // Menü
            padMenuTitle = new TextBlock { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 16) };
            padMenuList = new StackPanel();
            var menuCard = new Border
            {
                Width = 520,
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(22),
                Background = MakeBrush("#F2131722"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = new StackPanel { Children = { padMenuTitle, padMenuList } }
            };
            padMenuHost = new Grid { Visibility = Visibility.Collapsed, Background = MakeBrush("#B3000000") };
            padMenuHost.Children.Add(menuCard);
            padLayer.Children.Add(padMenuHost);

            WindowRoot.Children.Add(padLayer);
        }

        private void RefreshPadTabs()
        {
            if (padTabs == null) return;

            padTabs.Children.Clear();

            // Aktive Suche als eigener Reiter vorne
            if (padSearch.Length > 0)
            {
                var searchPill = new Border
                {
                    Padding = new Thickness(20, 9, 20, 9),
                    CornerRadius = new CornerRadius(22),
                    Margin = new Thickness(5, 0, 5, 0),
                    Child = new TextBlock { Text = "🔎 " + padSearch, FontSize = 17, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White }
                };
                searchPill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                padTabs.Children.Add(searchPill);
            }

            for (int i = 0; i < PadCategories.Length; i++)
            {
                bool active = i == padCategory && padSearch.Length == 0;
                var pill = new Border
                {
                    Padding = new Thickness(20, 9, 20, 9),
                    CornerRadius = new CornerRadius(22),
                    Margin = new Thickness(5, 0, 5, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = Loc.T(PadCategories[i]),
                        FontSize = 17,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = active ? System.Windows.Media.Brushes.White : MakeBrush("#9CA3AF")
                    }
                };
                if (active) pill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

                int index = i;
                pill.MouseLeftButtonUp += (s, e) => SetPadCategory(index);
                padTabs.Children.Add(pill);
            }
        }

        private void RefreshPadHints()
        {
            if (padHints == null) return;

            padHints.Children.Clear();
            // Beschriftung folgt dem Controller-Typ und der eigenen Tastenbelegung
            (string Glyph, string Word)[] keys =
            {
                (PadGlyph("confirm"), "Starten"), (PadGlyph("back"), "Zurück"), (PadGlyph("details"), "Optionen"), (PadGlyph("favorite"), "Favorit"),
                (PadGlyph("prevtab") + " / " + PadGlyph("nexttab"), "Bereich"), (PadGlyph("jumpleft") + " / " + PadGlyph("jumpright"), "Springen"),
                (PadGlyph("menu"), "Menü")
            };

            foreach (var (glyph, word) in keys)
            {
                var chip = new Border
                {
                    Padding = new Thickness(13, 7, 15, 7),
                    CornerRadius = new CornerRadius(11),
                    Background = MakeBrush("#22FFFFFF"),
                    Margin = new Thickness(7, 0, 7, 0)
                };
                var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                var glyphText = new TextBlock { Text = glyph, FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
                glyphText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                row.Children.Add(glyphText);
                row.Children.Add(new TextBlock { Text = Loc.T(word), FontSize = 16, Foreground = MakeBrush("#E5E7EB"), VerticalAlignment = System.Windows.VerticalAlignment.Center });
                chip.Child = row;
                padHints.Children.Add(chip);
            }
        }

        private void SetPadCategory(int index)
        {
            padCategory = ((index % PadCategories.Length) + PadCategories.Length) % PadCategories.Length;
            padSearch = string.Empty;
            RebuildPadItems();
            RefreshPadTabs();
            PlayUiSound("move");
        }

        private void RebuildPadItems()
        {
            if (padFlow == null) return;

            IEnumerable<PadItem> source = padSearch.Length > 0 ? SearchPadItems(padSearch) : padCategory switch
            {
                1 => allGames.Where(g => !g.Hidden && g.LastPlayed.HasValue).OrderByDescending(g => g.LastPlayed).Take(40).Select(PadItemFromGame),
                2 => allGames.Where(g => !g.Hidden && g.IsFavorite).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(PadItemFromGame),
                3 => allGames.Where(g => !g.Hidden && IsNewGame(g)).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(PadItemFromGame),
                4 => cachedApps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(PadItemFromApp),
                _ => allGames.Where(g => !g.Hidden).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).Select(PadItemFromGame)
            };

            padItems = source.ToList();
            padFlow.SetCount(padItems.Count, 0);
            PrefetchFlow(padFlow, padItems);
            if (padEmpty != null)
            {
                padEmpty.Text = padSearch.Length > 0 ? Loc.T($"Keine Treffer für „{padSearch}“.") : Loc.T("Hier ist noch nichts.");
                padEmpty.Visibility = padItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            OnPadSelectionChanged();
        }


        private void LaunchPadItem()
        {
            int index = padFlow?.SelectedIndex ?? -1;
            if (index < 0 || index >= padItems.Count) return;

            var item = padItems[index];
            PadRumble(70);

            if (item.Game != null)
            {
                // Das Spiel soll vorne landen: den Controller-Modus verlassen und nach dem Spiel wieder öffnen
                padResumeAfterGame = true;
                ToggleControllerMode();
                LaunchGame(item.Game);
            }
            else if (item.App != null)
            {
                ToggleControllerMode();
                LaunchApp(item.App);
            }
        }

        private void ResumePadAfterGame()
        {
            if (!padResumeAfterGame || activeSessions.Count > 0) return;

            padResumeAfterGame = false;
            if (!IsVisible) ShowFromTray();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            if (!controllerMode) ToggleControllerMode();
        }

        private void ToggleControllerMode()
        {
            controllerMode = !controllerMode;
            if (TitleBar != null) TitleBar.Visibility = controllerMode ? Visibility.Collapsed : Visibility.Visible;

            if (controllerMode)
            {
                if (!IsVisible) ShowFromTray();
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

                savedWindowState = WindowState;
                savedWindowStyle = WindowStyle;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                Topmost = true;
                Activate();
                ForceForeground();

                EnsurePadLayer();
                padCategory = allGames.Any(g => !g.Hidden && g.LastPlayed.HasValue) ? 1 : 0;
                RefreshPadTabs();
                RefreshPadHints();
                RebuildPadItems();

                padLayer!.BeginAnimation(UIElement.OpacityProperty, null);
                padLayer.Opacity = 1;
                padLayer.Visibility = Visibility.Visible;
                RootGrid.Visibility = Visibility.Collapsed;   // die normale Oberfläche darunter wird nicht mehr gezeichnet
                UpdatePadPill();

                padClockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                padClockTimer.Tick -= PadClockTick;
                padClockTimer.Tick += PadClockTick;
                padClockTimer.Start();
                PadClockTick(this, EventArgs.Empty);

                controllerTimer.Interval = TimeSpan.FromMilliseconds(25);
                padFast = true;
                NativeFeatures.timeBeginPeriod(1);
            }
            else
            {
                ClosePadMenu();
                if (padKeyboardOpen) ClosePadKeyboard();
                if (padVolumeOpen) ClosePadVolume();
                padSearch = string.Empty;
                RootGrid.Visibility = Visibility.Visible;
                if (padLayer != null) padLayer.Visibility = Visibility.Collapsed;
                padClockTimer?.Stop();

                WindowState = WindowState.Normal;
                WindowStyle = savedWindowStyle;
                WindowState = savedWindowState;
                Topmost = settings.AlwaysOnTop;

                controllerTimer.Interval = TimeSpan.FromMilliseconds(60);
                padFast = false;
                NativeFeatures.timeEndPeriod(1);
            }

            ApplySidebar(true);
            UpdatePadHint();
            FixMaximizedOverhang();
            PlayUiSound("select");
        }

        private void PadClockTick(object? sender, EventArgs e)
        {
            if (padClockText != null) padClockText.Text = DateTime.Now.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        // ───────────────────────────── Controller-Menüs ─────────────────────────────

        private void OpenPadMenu(string title, List<(string Label, Action Run)> entries)
        {
            if (padMenuHost == null || padMenuList == null || padMenuTitle == null || entries.Count == 0) return;

            padMenuEntries = entries;
            padMenuIndex = 0;
            padMenuOpen = true;
            padMenuTitle.Text = title;
            RenderPadMenu();
            padMenuHost.Visibility = Visibility.Visible;
        }

        private void RenderPadMenu()
        {
            if (padMenuList == null) return;

            padMenuList.Children.Clear();
            for (int i = 0; i < padMenuEntries.Count; i++)
            {
                var row = new Border
                {
                    Padding = new Thickness(18, 13, 18, 13),
                    CornerRadius = new CornerRadius(13),
                    Margin = new Thickness(0, 0, 0, 6),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Child = new TextBlock { Text = padMenuEntries[i].Label, FontSize = 20, Foreground = System.Windows.Media.Brushes.White }
                };
                if (i == padMenuIndex) row.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                padMenuList.Children.Add(row);
            }
        }

        private void MovePadMenu(int delta)
        {
            if (padMenuEntries.Count == 0) return;

            padMenuIndex = (padMenuIndex + delta + padMenuEntries.Count) % padMenuEntries.Count;
            RenderPadMenu();
            PlayUiSound("move");
            PadRumble(12);
        }

        private void ActivatePadMenu()
        {
            if (padMenuIndex < 0 || padMenuIndex >= padMenuEntries.Count) return;

            var entry = padMenuEntries[padMenuIndex];
            ClosePadMenu();
            entry.Run();
        }

        private void ClosePadMenu()
        {
            padMenuOpen = false;
            if (padMenuHost != null) padMenuHost.Visibility = Visibility.Collapsed;
        }

        private void OpenPadStartMenu()
        {
            var entries = new List<(string, Action)>
            {
                (Loc.T("Zurück zum Launcher"), ToggleControllerMode),
                (Loc.T("🔎  Suchen"), OpenPadKeyboard),
                (Loc.T("🔊  Lautstärke"), OpenPadVolume)
            };
            if (activeSessions.Count > 0) entries.Add((Loc.T("⏹  Spiel beenden"), PadEndGame));
            entries.Add((Loc.T(StreamerOn ? "Streamer-Modus ausschalten" : "Streamer-Modus einschalten"), ToggleStreamerMode));
            entries.Add((Loc.T("Empfohlene Tastenbelegung"), () => { ToggleControllerMode(); ShowPadMappingDialog(); }));
            entries.Add((Loc.T("Controller-Einstellungen"), () => { ToggleControllerMode(); OpenControllerSettings(); }));
            if (settings.PowerButtons)
            {
                entries.Add((Loc.T("🔄  PC neu starten"), () => PadPowerAction("restart")));
                entries.Add((Loc.T("⏻  PC herunterfahren"), () => PadPowerAction("shutdown")));
            }
            entries.Add((Loc.T("Launcher beenden"), ExitApplication));
            OpenPadMenu(Loc.T("Menü"), entries);
        }

        private void OpenPadGameMenu()
        {
            int index = padFlow?.SelectedIndex ?? -1;
            if (index < 0 || index >= padItems.Count) return;

            var item = padItems[index];
            var entries = new List<(string Label, Action Run)> { (Loc.T("▶  Starten"), LaunchPadItem) };

            if (item.Game != null)
            {
                var game = item.Game;
                entries.Add((Loc.T(game.IsFavorite ? "★  Aus Favoriten entfernen" : "☆  Zu Favoriten hinzufügen"), () => ToggleFavorite(game)));
                entries.Add((Loc.T("🙈  Verstecken"), () =>
                {
                    ToggleHidden(game);
                    RebuildPadItems();
                }
                ));
            }
            entries.Add((Loc.T("Abbrechen"), () => { }));

            OpenPadMenu(item.Name, entries);
        }

        private void OpenControllerSettings()
        {
            NavigateTo("settings");
            ChipCatControl.IsChecked = true;
        }

        // ───────────────────────────── Controller: Eingaben, Tastenkombination, Akku, Vibration ─────────────────────────────

        private void HandlePadInput(PadButton pressed)
        {
            if (padKeyboardOpen)
            {
                HandlePadKeyboard(pressed);
                return;
            }
            if (padVolumeOpen)
            {
                HandlePadVolume(pressed);
                return;
            }

            if (padMenuOpen)
            {
                if ((pressed & PadButton.Up) != 0) MovePadMenu(-1);
                if ((pressed & PadButton.Down) != 0) MovePadMenu(1);
                if ((pressed & PadButton.Confirm) != 0) ActivatePadMenu();
                if ((pressed & (PadButton.Back | PadButton.Start)) != 0) ClosePadMenu();
                return;
            }

            if (padFlow == null) return;

            if ((pressed & PadButton.Left) != 0) padFlow.Move(-1);
            if ((pressed & PadButton.Right) != 0) padFlow.Move(1);
            if ((pressed & PadButton.TriggerLeft) != 0) padFlow.Move(-10);
            if ((pressed & PadButton.TriggerRight) != 0) padFlow.Move(10);
            if ((pressed & (PadButton.Up | PadButton.PrevTab)) != 0) SetPadCategory(padCategory - 1);
            if ((pressed & (PadButton.Down | PadButton.NextTab)) != 0) SetPadCategory(padCategory + 1);
            if ((pressed & PadButton.Confirm) != 0) LaunchPadItem();
            if ((pressed & PadButton.Back) != 0)
            {
                // Bei aktiver Suche zuerst die Suche verlassen, erst dann den Controller-Modus
                if (padSearch.Length > 0) ClearPadSearch();
                else ToggleControllerMode();
            }
            if ((pressed & PadButton.Details) != 0) OpenPadGameMenu();
            if ((pressed & PadButton.Start) != 0) OpenPadStartMenu();

            if ((pressed & PadButton.Favorite) != 0)
            {
                int index = padFlow.SelectedIndex;
                if (index >= 0 && index < padItems.Count && padItems[index].Game is { } game)
                {
                    ToggleFavorite(game);
                    ShowToast(game.IsFavorite ? "★" : "☆", Loc.T(game.IsFavorite ? "Zu Favoriten hinzugefügt" : "Aus Favoriten entfernt"), game.Name, 3);
                }
            }
        }

        private bool PadKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            if (!controllerMode) return false;

            var map = e.Key switch
            {
                System.Windows.Input.Key.Left => PadButton.Left,
                System.Windows.Input.Key.Right => PadButton.Right,
                System.Windows.Input.Key.Up => PadButton.Up,
                System.Windows.Input.Key.Down => PadButton.Down,
                System.Windows.Input.Key.PageUp => PadButton.PrevTab,
                System.Windows.Input.Key.PageDown => PadButton.NextTab,
                System.Windows.Input.Key.Enter => PadButton.Confirm,
                System.Windows.Input.Key.Escape => PadButton.Back,
                System.Windows.Input.Key.Space => PadButton.Start,
                System.Windows.Input.Key.F => PadButton.Favorite,
                System.Windows.Input.Key.O => PadButton.Details,
                _ => PadButton.None
            };

            if (map == PadButton.None) return false;
            HandlePadInput(map);
            return true;
        }

        private PadButton ApplyPadRepeat(PadButton pad, PadButton pressed)
        {
            const PadButton directions = PadButton.Left | PadButton.Right | PadButton.Up | PadButton.Down;
            var now = DateTime.Now;

            if ((pressed & directions) != 0)
            {
                padRepeatSince = now;
                padRepeatLast = now;
                return pressed;
            }

            PadButton held = pad & directions;
            if (held == PadButton.None) return pressed;

            double interval = settings.PadRepeat switch { 1 => 170, 3 => 55, _ => 100 };
            if ((now - padRepeatSince).TotalMilliseconds > 380 && (now - padRepeatLast).TotalMilliseconds >= interval)
            {
                padRepeatLast = now;
                return pressed | held;
            }
            return pressed;
        }

        /// <summary>Startet oder beendet den Controller-Modus per Tastenkombination, auch wenn der Launcher im Hintergrund liegt.</summary>
        private bool CheckPadCombo(PadButton pad, PadButton pressed)
        {
            if (!settings.PadCombo)
            {
                padStartHeld = null;
                return false;
            }
            if (activeSessions.Count > 0 && !settings.PadComboInGame) return false;

            bool fire = false;
            switch (settings.PadComboMode)
            {
                case "bumpers":
                    fire = (pad & (PadButton.PrevTab | PadButton.NextTab | PadButton.Start)) == (PadButton.PrevTab | PadButton.NextTab | PadButton.Start)
                           && pressed != PadButton.None;
                    break;

                case "hold":
                    if (controllerMode) return false;
                    if ((pad & PadButton.Start) != 0 && (pad & ~PadButton.Start) == PadButton.None)
                    {
                        padStartHeld ??= DateTime.Now;
                        if ((DateTime.Now - padStartHeld.Value).TotalMilliseconds >= 1500)
                        {
                            fire = true;
                            padStartHeld = null;
                        }
                    }
                    else
                    {
                        padStartHeld = null;
                    }
                    break;

                default:
                    fire = (pad & (PadButton.Select | PadButton.Start)) == (PadButton.Select | PadButton.Start) && pressed != PadButton.None;
                    break;
            }

            if (!fire) return false;

            ToggleControllerMode();
            return true;
        }

        private void TrackPadConnection(bool connected)
        {
            if (!connected)
            {
                padWasConnected = false;
                return;
            }
            if (padWasConnected) return;
            padWasConnected = true;

            string layout = ResolveLayout();
            if (!settings.PadSeenLayouts.Contains(layout))
            {
                settings.PadSeenLayouts.Add(layout);
                SaveSettings();
                ShowToast("🎮", "Controller erkannt", padDescription + "\n" + Loc.T("Klicke hier für die empfohlene Tastenbelegung."), 14, ShowPadMappingDialog, true);
            }

            if (settings.PadAutoStart && !controllerMode && activeSessions.Count == 0 && IsVisible) ToggleControllerMode();
        }

        private void PadRumble(int milliseconds)
        {
            if (!settings.PadVibrate || padSlot < 0) return;

            try
            {
                var on = new NativeFeatures.XInputVibration { LeftMotor = 9000, RightMotor = 14000 };
                NativeFeatures.XInputSetState((uint)padSlot, ref on);

                padRumbleTimer ??= new DispatcherTimer();
                padRumbleTimer.Stop();
                padRumbleTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
                padRumbleTimer.Tick -= PadRumbleStop;
                padRumbleTimer.Tick += PadRumbleStop;
                padRumbleTimer.Start();
            }
            catch { }
        }

        private void PadRumbleStop(object? sender, EventArgs e)
        {
            padRumbleTimer?.Stop();
            try
            {
                var off = new NativeFeatures.XInputVibration();
                if (padSlot >= 0) NativeFeatures.XInputSetState((uint)padSlot, ref off);
            }
            catch { }
        }

        // ───────────────────────────── Empfohlene Tastenbelegung ─────────────────────────────

        private void ShowPadMappingDialog()
        {
            string layout = ResolveLayout();
            bool custom = settings.PadMappings.ContainsKey(layout);
            (string Action, string Button)[] rows =
            {
                ("Starten und bestätigen", PadGlyph("confirm")), ("Zurück", PadGlyph("back")), ("Optionen zum Spiel", PadGlyph("details")), ("Favorit", PadGlyph("favorite")),
                ("Bereich wechseln", PadGlyph("prevtab") + " / " + PadGlyph("nexttab")), ("Zehn Spiele springen", PadGlyph("jumpleft") + " / " + PadGlyph("jumpright")),
                ("Menü", PadGlyph("menu")), ("Controller-Modus an und aus", PadGlyph("view") + "  &  " + PadGlyph("menu"))
            };

            var dialog = CreateDialog(custom ? "Deine Tastenbelegung" : "Empfohlene Tastenbelegung", 560, out var panel);
            string kind = layout == "ps" ? "PlayStation" : layout == "nintendo" ? "Nintendo" : "Xbox";

            panel.Children.Add(new TextBlock { Text = "🎮", FontSize = 44, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T(custom ? "Deine Tastenbelegung" : "Empfohlene Tastenbelegung"),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = padDescription.Length > 0 ? padDescription + "  ·  " + kind : kind,
                Foreground = BrushSubtle,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 16)
            });

            foreach (var (action, button) in rows)
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 9) };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(new TextBlock { Text = Loc.T(action), Foreground = MakeBrush("#D1D5DB") });

                var tag = new Border { Padding = new Thickness(12, 4, 12, 4), CornerRadius = new CornerRadius(9), Background = MakeBrush("#26FFFFFF") };
                var tagText = new TextBlock { Text = button, FontWeight = FontWeights.Bold };
                tagText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                tag.Child = tagText;
                Grid.SetColumn(tag, 1);
                line.Children.Add(tag);
                panel.Children.Add(line);
            }

            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("Stimmt die Belegung nicht mit deinem Controller überein, wähle sie unter Einstellungen → Steuerung selbst aus."),
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 16)
            });

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var settingsButton = new System.Windows.Controls.Button { Content = Loc.T("Einstellungen öffnen"), Margin = new Thickness(0, 0, 10, 0) };
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), IsCancel = true, IsDefault = true, Padding = new Thickness(24, 8, 24, 8) };
            close.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
            buttons.Children.Add(settingsButton);
            buttons.Children.Add(close);
            panel.Children.Add(buttons);

            settingsButton.Click += (s, e) =>
            {
                dialog.DialogResult = true;
                OpenControllerSettings();
            };
            dialog.ShowDialog();
        }

        private void BtnPadMapping_Click(object sender, RoutedEventArgs e) => ShowPadMappingDialog();

        private void BtnPadStart_Click(object sender, RoutedEventArgs e)
        {
            if (!controllerMode) ToggleControllerMode();
        }

        private void ReadExtra14Settings()
        {
            settings.PadCombo = ChkPadCombo.IsChecked == true;
            settings.PadComboMode = ChipPadComboBumpers.IsChecked == true ? "bumpers" : ChipPadComboHold.IsChecked == true ? "hold" : "backstart";
            settings.PadComboInGame = ChkPadComboInGame.IsChecked == true;
            settings.PadVibrate = ChkPadVibrate.IsChecked == true;
            settings.PadRepeat = ChipPadRepSlow.IsChecked == true ? 1 : ChipPadRepFast.IsChecked == true ? 3 : 2;
            settings.PadAutoStart = ChkPadAutoStart.IsChecked == true;
        }

        private void PopulateExtra14Settings()
        {
            ChkPadCombo.IsChecked = settings.PadCombo;
            ChipPadComboBackStart.IsChecked = settings.PadComboMode is not ("bumpers" or "hold");
            ChipPadComboBumpers.IsChecked = settings.PadComboMode == "bumpers";
            ChipPadComboHold.IsChecked = settings.PadComboMode == "hold";
            ChkPadComboInGame.IsChecked = settings.PadComboInGame;
            ChkPadVibrate.IsChecked = settings.PadVibrate;
            ChipPadRepSlow.IsChecked = settings.PadRepeat == 1;
            ChipPadRepMid.IsChecked = settings.PadRepeat is not (1 or 3);
            ChipPadRepFast.IsChecked = settings.PadRepeat == 3;
            ChkPadAutoStart.IsChecked = settings.PadAutoStart;
        }

        // ───────────────────────────── Controller: Abfrage im eigenen System.Threading.Thread ─────────────────────────────

        private System.Threading.Thread? padThread;
        private volatile bool padThreadRun;
        private volatile bool padFast;
        private int padThreadSlot = -1;
        private int padThreadScanned;
        private int padThreadState;
        private int padSignalPending;
        private readonly System.Collections.Concurrent.ConcurrentQueue<(int Edges, long Stamp)> padEdges = new();
        private System.Windows.Threading.Dispatcher? padDispatcher;

        private void EnsurePadThread()
        {
            if (padThread != null || xinputMissing) return;

            padDispatcher = Dispatcher;
            padThreadRun = true;
            padThread = new System.Threading.Thread(PadThreadLoop)
            {
                IsBackground = true,
                Name = "DFP-Controller",
                Priority = System.Threading.ThreadPriority.AboveNormal
            };
            padThread.Start();
        }

        private static PadButton MapXInput(NativeFeatures.XInputState state, ref PadButton stick)
        {
            var g = state.Gamepad;
            ushort b = g.Buttons;
            PadButton pad = PadButton.None;

            if ((b & 0x0001) != 0) pad |= PadButton.Up;
            if ((b & 0x0002) != 0) pad |= PadButton.Down;
            if ((b & 0x0004) != 0) pad |= PadButton.Left;
            if ((b & 0x0008) != 0) pad |= PadButton.Right;

            // Stick: nur die stärkere Achse zählt (kein versehentliches Hoch/Runter beim Blättern), mit Hysterese gegen Flackern
            int ax = Math.Abs((int)g.ThumbLX), ay = Math.Abs((int)g.ThumbLY);
            int need = stick != PadButton.None ? 12000 : 20000;
            PadButton next = PadButton.None;
            if (ax >= ay && ax > need) next = g.ThumbLX < 0 ? PadButton.Left : PadButton.Right;
            else if (ay > ax && ay > need) next = g.ThumbLY > 0 ? PadButton.Up : PadButton.Down;
            stick = next;
            pad |= next;

            if ((b & 0x1000) != 0) pad |= PadButton.Confirm;
            if ((b & 0x2000) != 0) pad |= PadButton.Back;
            if ((b & 0x4000) != 0) pad |= PadButton.Details;
            if ((b & 0x8000) != 0) pad |= PadButton.Favorite;
            if ((b & 0x0100) != 0) pad |= PadButton.PrevTab;
            if ((b & 0x0200) != 0) pad |= PadButton.NextTab;
            if ((b & 0x0010) != 0) pad |= PadButton.Start;
            if ((b & 0x0020) != 0) pad |= PadButton.Select;
            if (g.LeftTrigger > 120) pad |= PadButton.TriggerLeft;
            if (g.RightTrigger > 120) pad |= PadButton.TriggerRight;
            return pad;
        }



        private void ProcessPad(PadButton pad, PadButton pressed)
        {
            if (CheckPadCombo(pad, pressed)) return;
            if (controllerMode) pressed = ApplyPadRepeat(pad, pressed);
            if (pressed == PadButton.None || SpotlightLayer.Visibility == Visibility.Visible) return;
            if (!controllerMode && !IsActive) return;

            TrackKonamiPad(pressed);
            if (!controllerMode) return;

            HandlePadInput(pressed);
        }

        private void ForceForeground()
        {
            try
            {
                // Windows erlaubt das Vordergrund-Holen nur nach einer Eingabe: eine kurze Alt-Taste genügt
                NativeExtras.keybd_event(0x12, 0, 0, UIntPtr.Zero);
                NativeExtras.keybd_event(0x12, 0, 2, UIntPtr.Zero);
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle != IntPtr.Zero) NativeFeatures.SetForegroundWindow(handle);
            }
            catch { }
        }

        // ───────────────────────────── Controller: WinMM-Geräte im eigenen Thread, Latenzmessung ─────────────────────────────

        private string padThreadDescription = string.Empty;
        private int padThreadKind;   // 0 = keiner, 1 = Xbox (XInput), 2 = anderes Gamepad (WinMM)
        private double padLatencyMax, padLatencySum;
        private int padLatencyCount, padLatencySpikes;

        private bool TryReadWinMm(uint id, out PadButton mapped)
        {
            mapped = PadButton.None;

            var info = new NativePad.JoyInfoEx
            {
                dwSize = (uint)Interop.Marshal.SizeOf<NativePad.JoyInfoEx>(),
                dwFlags = 0xFF
            };

            uint result;
            try { result = NativePad.joyGetPosEx(id, ref info); }
            catch { return false; }
            if (result != 0) return false;

            mapped = MapJoystick(info);
            return true;
        }

        private bool PollWinMm(ref uint preferred, out PadButton pad)
        {
            pad = PadButton.None;

            uint count;
            try { count = NativePad.joyGetNumDevs(); }
            catch { return false; }
            if (count == 0) return false;
            if (count > 16) count = 16;

            // Zuerst das zuletzt benutzte Gerät, nur bei Verlust werden alle Nummern durchsucht
            if (preferred < count && TryReadWinMm(preferred, out pad)) return true;

            preferred = uint.MaxValue;
            for (uint id = 0; id < count; id++)
            {
                if (!TryReadWinMm(id, out pad)) continue;

                preferred = id;
                padThreadDescription = DescribeJoystick(id);
                return true;
            }

            padThreadDescription = string.Empty;
            return false;
        }

        private void ShowPadLatency()
        {
            if (padLatencyCount == 0)
            {
                ShowToast("🎮", "Controller-Latenz", "Noch keine Eingaben gemessen. Drücke ein paar Tasten.", 5);
                return;
            }

            ShowToast("🎮", "Controller-Latenz",
                $"Ø {padLatencySum / padLatencyCount:F1} ms, Spitze {padLatencyMax:F1} ms ({padLatencyCount} Eingaben, {padLatencySpikes} über 30 ms)", 8);
            padLatencyMax = 0;
            padLatencySum = 0;
            padLatencyCount = 0;
            padLatencySpikes = 0;
        }

        // ───────────────────────────── Controller-Akku und Kabel-Status ─────────────────────────────

        private int padLastWirelessLevel = -1;

        private string BatteryText(int level) => level switch
        {
            3 => "🔋 " + Loc.T("Akku") + ": " + Loc.T("Voll"),
            2 => "🔋 " + Loc.T("Akku") + ": " + Loc.T("Mittel"),
            1 => "🪫 " + Loc.T("Akku") + ": " + Loc.T("Niedrig"),
            _ => "🪫 " + Loc.T("Akku") + ": " + Loc.T("Leer")
        };

        private static string BatteryColor(int level) => level switch
        {
            3 => "#34D399",
            2 => "#A3E635",
            1 => "#F59E0B",
            _ => "#EF4444"
        };

        private void UpdatePadPill()
        {
            if (padBatteryPill == null || padBatteryText == null) return;

            if (padDescription.Length == 0)
            {
                padBatteryPill.Visibility = Visibility.Collapsed;
                return;
            }

            string text, color;
            if (padBatteryWired)
            {
                text = "🔌 " + Loc.T("Kabelgebunden an");
                if (padLastWirelessLevel >= 0) text += "  ·  " + BatteryText(padLastWirelessLevel) + " (" + Loc.T("zuletzt") + ")";
                color = "#60A5FA";
            }
            else if (padBatteryLevel >= 0)
            {
                text = BatteryText(padBatteryLevel);
                color = BatteryColor(padBatteryLevel);
            }
            else
            {
                text = "🎮 " + Loc.T("Verbunden");
                color = "#9CA3AF";
            }

            if (padBatteryText.Text != text)
            {
                padBatteryText.Text = text;
                padBatteryText.Foreground = MakeBrush(color);
            }
            padBatteryPill.Visibility = Visibility.Visible;
        }

        private void PadThreadLoop()
        {
            int slot = -1;
            uint winmm = uint.MaxValue;
            int kind = 0;
            PadButton last = PadButton.None, stick = PadButton.None;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long lastScan = -10000;

            void Publish(PadButton pad)
            {
                PadButton pressed = pad & ~last;
                last = pad;
                System.Threading.Volatile.Write(ref padThreadState, (int)pad);
                if (pressed == PadButton.None) return;

                padEdges.Enqueue(((int)pressed, System.Diagnostics.Stopwatch.GetTimestamp()));
                if (System.Threading.Interlocked.Exchange(ref padSignalPending, 1) == 0)
                    padDispatcher?.BeginInvoke(DispatcherPriority.Send, new Action(ProcessPadEdges));
            }

            void Reset()
            {
                slot = -1;
                kind = 0;
                last = stick = PadButton.None;
                System.Threading.Volatile.Write(ref padThreadSlot, -1);
                System.Threading.Volatile.Write(ref padThreadKind, 0);
                System.Threading.Volatile.Write(ref padThreadState, 0);
            }

            while (padThreadRun)
            {
                try
                {
                    if (!settings.ControllerSupport)
                    {
                        Reset();
                        System.Threading.Thread.Sleep(300);
                        continue;
                    }

                    // Xbox-Controller: sehr schnell abfragen
                    if (slot >= 0)
                    {
                        if (NativeFeatures.XInputGetState((uint)slot, out var state) != 0)
                        {
                            Reset();
                            lastScan = -10000;
                            continue;
                        }

                        Publish(RemapPad(MapXInput(state, ref stick)));
                        System.Threading.Thread.Sleep(padFast ? 2 : 8);
                        continue;
                    }

                    // Leere Anschlüsse sind langsam abzufragen: nur etwa einmal pro Sekunde suchen
                    if (clock.ElapsedMilliseconds - lastScan >= 1000)
                    {
                        lastScan = clock.ElapsedMilliseconds;
                        for (uint s = 0; s < 4; s++)
                        {
                            if (NativeFeatures.XInputGetState(s, out _) == 0)
                            {
                                slot = (int)s;
                                break;
                            }
                        }

                        System.Threading.Volatile.Write(ref padThreadScanned, 1);
                        if (slot >= 0)
                        {
                            kind = 1;
                            winmm = uint.MaxValue;
                            last = stick = PadButton.None;
                            System.Threading.Volatile.Write(ref padThreadSlot, slot);
                            System.Threading.Volatile.Write(ref padThreadKind, 1);
                            continue;
                        }
                    }

                    // Andere Gamepads (zum Beispiel PlayStation) über WinMM, ebenfalls im Hintergrund
                    if (System.Threading.Volatile.Read(ref padThreadScanned) == 1 && PollWinMm(ref winmm, out var other))
                    {
                        if (kind != 2)
                        {
                            kind = 2;
                            last = stick = PadButton.None;
                            System.Threading.Volatile.Write(ref padThreadSlot, -1);
                            System.Threading.Volatile.Write(ref padThreadKind, 2);
                        }

                        Publish(RemapPad(other));
                        System.Threading.Thread.Sleep(padFast ? 3 : 10);
                        continue;
                    }

                    if (kind != 0) Reset();
                    System.Threading.Thread.Sleep(120);
                }
                catch (DllNotFoundException)
                {
                    xinputMissing = true;
                    break;
                }
                catch (EntryPointNotFoundException)
                {
                    xinputMissing = true;
                    break;
                }
                catch
                {
                    System.Threading.Thread.Sleep(50);
                }
            }

            padThread = null;
        }

        /// <summary>Tastendrücke kommen sofort aus dem Controller-Thread, ohne auf den nächsten Takt zu warten.</summary>
        private void ProcessPadEdges()
        {
            System.Threading.Interlocked.Exchange(ref padSignalPending, 0);

            PadButton edges = PadButton.None;
            long oldest = long.MaxValue;
            while (padEdges.TryDequeue(out var item))
            {
                edges |= (PadButton)item.Edges;
                if (item.Stamp < oldest) oldest = item.Stamp;
            }
            if (edges == PadButton.None) return;

            double milliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - oldest) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            padLatencySum += milliseconds;
            padLatencyCount++;
            if (milliseconds > padLatencyMax) padLatencyMax = milliseconds;
            if (milliseconds > 30) padLatencySpikes++;

            PadButton pad = (PadButton)System.Threading.Volatile.Read(ref padThreadState);
            ProcessPad(pad | edges, edges);
        }

        private void ControllerTick()
        {
            if (!settings.ControllerSupport) return;
            EnsurePadThread();

            int kind = System.Threading.Volatile.Read(ref padThreadKind);
            if (kind == 0)
            {
                TrackPadConnection(false);
                if (padDescription.Length > 0)
                {
                    padDescription = string.Empty;
                    padSlot = -1;
                    UpdatePadStatus();
                    UpdatePadPill();
                }
                return;
            }

            // Abfrage und Tastendrücke laufen im Hintergrund, hier nur Status, Akku und Gedrückthalten
            PadButton pad = (PadButton)System.Threading.Volatile.Read(ref padThreadState);
            if (kind == 1)
            {
                padSlot = System.Threading.Volatile.Read(ref padThreadSlot);
                padDescription = $"Xbox-Controller (Slot {padSlot + 1})";
            }
            else
            {
                padSlot = -1;
                padDescription = padThreadDescription.Length > 0 ? padThreadDescription : "Gamepad";
            }

            UpdatePadStatus();
            TickPadBattery();
            TrackPadConnection(true);
            if (padLayer != null && padLayer.Visibility == Visibility.Visible) UpdatePadPill();

            ProcessPad(pad, PadButton.None);
        }

        // ═════════════════════════════ Controller-Extras ═════════════════════════════

        private void InitExtras19()
        {
            RebuildPadRemap();

            // „Als Konsole starten“: Startparameter --controller öffnet direkt den Controller-Modus
            if (Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--controller", StringComparison.OrdinalIgnoreCase)))
            {
                Loaded += (s, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!controllerMode) ToggleControllerMode();
                }), DispatcherPriority.ApplicationIdle);
            }
        }

        // ───────── Eigene Tastenbelegung ─────────

        // Jede physische Taste hat eine Standard-Bedeutung (so liefern MapXInput und MapJoystick sie)
        private static readonly string[] PadSlotKeys = { "a", "b", "x", "y", "lb", "rb", "lt", "rt", "start", "select" };
        private static readonly PadButton[] PadSlotBits =
        {
            PadButton.Confirm, PadButton.Back, PadButton.Details, PadButton.Favorite, PadButton.PrevTab,
            PadButton.NextTab, PadButton.TriggerLeft, PadButton.TriggerRight, PadButton.Start, PadButton.Select
        };

        // Aktionen in derselben Reihenfolge wie die Tasten: Aktion i liegt standardmäßig auf Taste i
        private static readonly (string Key, string Title)[] PadActions =
        {
            ("confirm", "Starten und bestätigen"), ("back", "Zurück"), ("details", "Optionen zum Spiel"), ("favorite", "Favorit"),
            ("prevtab", "Bereich links"), ("nexttab", "Bereich rechts"), ("jumpleft", "Zehn Spiele zurück"), ("jumpright", "Zehn Spiele vor"),
            ("menu", "Menü"), ("view", "Ansicht (für die Tastenkombination)")
        };

        private static readonly string[] PadLayouts = { "xbox", "ps", "nintendo" };

        private static string[] PadSlotLabels(string layout) => layout switch
        {
            "ps" => new[] { "✕", "○", "□", "△", "L1", "R1", "L2", "R2", "OPTIONS", "SHARE" },
            "nintendo" => new[] { "B", "A", "Y", "X", "L", "R", "ZL", "ZR", "+", "−" },
            _ => new[] { "A", "B", "X", "Y", "LB", "RB", "LT", "RT", "☰", "⧉" }
        };

        private int[]?[] padRemapTables = new int[]?[3];
        private string padRemapEditLayout = string.Empty;

        private static bool IsValidPadMapping(Dictionary<string, string>? mapping)
        {
            if (mapping == null) return false;
            if (!PadActions.All(a => mapping.TryGetValue(a.Key, out var slot) && PadSlotKeys.Contains(slot))) return false;
            return PadActions.Select(a => mapping[a.Key]).Distinct().Count() == PadActions.Length;
        }

        private static Dictionary<string, string> DefaultPadMapping()
            => PadActions.Select((a, i) => (a.Key, Slot: PadSlotKeys[i])).ToDictionary(x => x.Key, x => x.Slot);

        private Dictionary<string, string> PadMappingFor(string layout)
            => settings.PadMappings.TryGetValue(layout, out var mapping) && IsValidPadMapping(mapping) ? mapping : DefaultPadMapping();

        /// <summary>Taste (Beschriftung), auf der eine Aktion beim aktuellen Controller-Typ liegt.</summary>
        private string PadGlyph(string action)
        {
            string layout = ResolveLayout();
            string slot = PadMappingFor(layout)[action];
            return PadSlotLabels(layout)[Array.IndexOf(PadSlotKeys, slot)];
        }

        /// <summary>Baut die Umsortier-Tabellen für den Controller-Thread neu (null = empfohlene Belegung).</summary>
        private void RebuildPadRemap()
        {
            var tables = new int[]?[PadLayouts.Length];
            for (int l = 0; l < PadLayouts.Length; l++)
            {
                if (!settings.PadMappings.TryGetValue(PadLayouts[l], out var mapping) || !IsValidPadMapping(mapping)) continue;

                var table = new int[PadSlotKeys.Length];
                bool identity = true;
                for (int a = 0; a < PadActions.Length; a++)
                {
                    int slot = Array.IndexOf(PadSlotKeys, mapping[PadActions[a].Key]);
                    table[slot] = (int)PadSlotBits[a];
                    if (slot != a) identity = false;
                }
                if (!identity) tables[l] = table;
            }
            System.Threading.Volatile.Write(ref padRemapTables, tables);
        }

        /// <summary>Läuft im Controller-Thread: ordnet die gedrückten Tasten nach der eigenen Belegung um. Sehr schnell, ohne Speicheranforderung.</summary>
        private PadButton RemapPad(PadButton pad)
        {
            var tables = System.Threading.Volatile.Read(ref padRemapTables);
            int index = ResolveLayout() switch { "ps" => 1, "nintendo" => 2, _ => 0 };
            int[]? table = tables[index];
            if (table == null || pad == PadButton.None) return pad;

            PadButton result = pad & (PadButton.Up | PadButton.Down | PadButton.Left | PadButton.Right);
            for (int i = 0; i < PadSlotBits.Length; i++)
            {
                if ((pad & PadSlotBits[i]) != 0) result |= (PadButton)table[i];
            }
            return result;
        }

        private void PopulateControllerExtras()
        {
            if (ChkConsoleStart == null) return;

            bool before = isLoadingSettings;
            isLoadingSettings = true;
            try
            {
                ChkConsoleStart.IsChecked = settings.ConsoleStart;
                if (padRemapEditLayout.Length == 0) padRemapEditLayout = ResolveLayout();
                ChipRemapXbox.IsChecked = padRemapEditLayout == "xbox";
                ChipRemapPs.IsChecked = padRemapEditLayout == "ps";
                ChipRemapNintendo.IsChecked = padRemapEditLayout == "nintendo";
            }
            finally
            {
                isLoadingSettings = before;
            }
            RebuildPadRemap();
            RenderPadRemap();
        }

        private void PadRemapLayout_Checked(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings || sender is not FrameworkElement { Tag: string layout }) return;
            padRemapEditLayout = layout;
            RenderPadRemap();
        }

        private void RenderPadRemap()
        {
            if (PadRemapPanel == null) return;
            PadRemapPanel.Children.Clear();

            string layout = padRemapEditLayout.Length > 0 ? padRemapEditLayout : ResolveLayout();
            var mapping = PadMappingFor(layout);
            var labels = PadSlotLabels(layout);

            foreach (var (key, title) in PadActions)
            {
                string action = key;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = Loc.T(title), Foreground = MakeBrush("#D1D5DB"), VerticalAlignment = System.Windows.VerticalAlignment.Center });

                int slotIndex = Array.IndexOf(PadSlotKeys, mapping[action]);
                bool changed = slotIndex != Array.FindIndex(PadActions, a => a.Key == action);
                var button = new System.Windows.Controls.Button
                {
                    Content = labels[slotIndex] + "  ▾",
                    MinWidth = 110,
                    Padding = new Thickness(14, 6, 14, 6),
                    ToolTip = Loc.T("Taste wählen")
                };
                if (changed) button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentBrush");
                button.Click += (s, e) =>
                {
                    var menu = CreateMenu();
                    for (int i = 0; i < PadSlotKeys.Length; i++)
                    {
                        string slot = PadSlotKeys[i];
                        string owner = PadActions.First(a => mapping[a.Key] == slot).Title;
                        var item = new System.Windows.Controls.MenuItem
                        {
                            Header = labels[i] + "   ·   " + Loc.T(owner),
                            IsChecked = mapping[action] == slot
                        };
                        item.Click += (s2, e2) => AssignPadSlot(layout, action, slot);
                        menu.Items.Add(item);
                    }
                    menu.PlacementTarget = button;
                    menu.IsOpen = true;
                };
                Grid.SetColumn(button, 1);
                row.Children.Add(button);
                PadRemapPanel.Children.Add(row);
            }
        }

        /// <summary>Legt eine Aktion auf eine Taste. Ist die Taste schon belegt, tauschen beide Aktionen ihre Tasten.</summary>
        private void AssignPadSlot(string layout, string action, string slot)
        {
            var mapping = new Dictionary<string, string>(PadMappingFor(layout));
            string oldSlot = mapping[action];
            if (oldSlot == slot) return;

            string? other = mapping.FirstOrDefault(p => p.Value == slot && p.Key != action).Key;
            if (other != null) mapping[other] = oldSlot;
            mapping[action] = slot;

            settings.PadMappings[layout] = mapping;
            SaveSettings();
            RebuildPadRemap();
            RenderPadRemap();
            RefreshPadHints();
        }

        private void BtnPadRemapReset_Click(object sender, RoutedEventArgs e)
        {
            string layout = padRemapEditLayout.Length > 0 ? padRemapEditLayout : ResolveLayout();
            settings.PadMappings.Remove(layout);
            SaveSettings();
            RebuildPadRemap();
            RenderPadRemap();
            RefreshPadHints();
            ShowToast("🎮", "Tastenbelegung", "Die empfohlene Belegung gilt wieder.", 3);
        }

        // ───────── Als Konsole starten ─────────

        private void ChkConsoleStart_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;

            settings.ConsoleStart = ChkConsoleStart.IsChecked == true;
            SaveSettings();
            try
            {
                if (settings.ConsoleStart)
                {
                    SetAutostart(true, true);
                    bool before = isLoadingSettings;
                    isLoadingSettings = true;
                    try { ChkAutostart.IsChecked = true; }
                    finally { isLoadingSettings = before; }
                }
                else if (IsAutostartEnabled())
                {
                    SetAutostart(true, false);   // normaler Autostart bleibt, nur ohne Controller-Modus
                }
            }
            catch (Exception ex)
            {
                Msg($"Autostart konnte nicht geändert werden:\n{ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ───────── Bildschirmtastatur und Suche im Controller-Modus ─────────

        private static readonly string[] PadKeyRows = { "1234567890", "QWERTZUIOP", "ASDFGHJKL-", "YXCVBNMÄÖÜ" };
        private string padSearch = string.Empty;
        private bool padKeyboardOpen;
        private int padKeyRow, padKeyCol;
        private Grid? padKeyboardHost;
        private TextBlock? padSearchText, padKeyboardHint;
        private readonly List<List<Border>> padKeyCells = new();

        private void EnsurePadKeyboard()
        {
            if (padKeyboardHost != null || padLayer == null) return;

            padSearchText = new TextBlock { FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 16), TextTrimming = TextTrimming.CharacterEllipsis };
            var rows = new StackPanel();
            padKeyCells.Clear();

            Border Key(string text, double width)
            {
                var cell = new Border
                {
                    Width = width,
                    Height = 54,
                    Margin = new Thickness(4),
                    CornerRadius = new CornerRadius(10),
                    Background = MakeBrush("#26FFFFFF"),
                    Child = new TextBlock { Text = text, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center }
                };
                return cell;
            }

            foreach (string keys in PadKeyRows)
            {
                var line = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                var cells = new List<Border>();
                foreach (char c in keys)
                {
                    var cell = Key(c.ToString(), 62);
                    cells.Add(cell);
                    line.Children.Add(cell);
                }
                padKeyCells.Add(cells);
                rows.Children.Add(line);
            }

            // Letzte Reihe: Leerzeichen, Löschen, Fertig
            var special = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var specialCells = new List<Border> { Key(Loc.T("Leerzeichen"), 270), Key("⌫ " + Loc.T("Löschen"), 196), Key("✓ " + Loc.T("Fertig"), 196) };
            foreach (var cell in specialCells) special.Children.Add(cell);
            padKeyCells.Add(specialCells);
            rows.Children.Add(special);

            padKeyboardHint = new TextBlock { FontSize = 15, Foreground = MakeBrush("#9CA3AF"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };

            var card = new Border
            {
                Padding = new Thickness(26, 22, 26, 20),
                CornerRadius = new CornerRadius(22),
                Background = MakeBrush("#F2131722"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 40),
                Child = new StackPanel { Children = { padSearchText, rows, padKeyboardHint } }
            };

            padKeyboardHost = new Grid { Visibility = Visibility.Collapsed };
            padKeyboardHost.Children.Add(card);
            padLayer.Children.Add(padKeyboardHost);
        }

        private void OpenPadKeyboard()
        {
            EnsurePadKeyboard();
            if (padKeyboardHost == null) return;

            padKeyboardOpen = true;
            padKeyRow = 1;
            padKeyCol = 0;
            padKeyboardHost.Visibility = Visibility.Visible;
            if (padKeyboardHint != null)
                padKeyboardHint.Text = $"{PadGlyph("confirm")} {Loc.T("Taste")}   ·   {PadGlyph("details")} {Loc.T("Löschen")}   ·   {PadGlyph("favorite")} {Loc.T("Leerzeichen")}   ·   {PadGlyph("back")} {Loc.T("Schließen")}   ·   {PadGlyph("jumpright")} {Loc.T("Fertig")}";
            UpdatePadKeyboard();
        }

        private void ClosePadKeyboard()
        {
            padKeyboardOpen = false;
            if (padKeyboardHost != null) padKeyboardHost.Visibility = Visibility.Collapsed;
            RefreshPadTabs();
        }

        private void UpdatePadKeyboard()
        {
            if (padSearchText != null) padSearchText.Text = "🔎  " + (padSearch.Length > 0 ? padSearch + "▏" : Loc.T("Spiel suchen ..."));

            for (int r = 0; r < padKeyCells.Count; r++)
            {
                for (int c = 0; c < padKeyCells[r].Count; c++)
                {
                    var cell = padKeyCells[r][c];
                    if (r == padKeyRow && c == padKeyCol) cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    else cell.Background = MakeBrush("#26FFFFFF");
                }
            }
        }

        /// <summary>Spalte in der letzten Reihe (3 breite Tasten) bzw. zurück in eine normale Reihe (10 Tasten).</summary>
        private static int MapKeyColumn(int fromRow, int toRow, int col, int lastRow)
        {
            if (toRow == lastRow && fromRow != lastRow) return col <= 3 ? 0 : col <= 6 ? 1 : 2;
            if (fromRow == lastRow && toRow != lastRow) return col switch { 0 => 2, 1 => 5, _ => 8 };
            return col;
        }

        private void HandlePadKeyboard(PadButton pressed)
        {
            int lastRow = padKeyCells.Count - 1;
            bool moved = false;

            if ((pressed & (PadButton.Up | PadButton.Down)) != 0)
            {
                int target = Math.Clamp(padKeyRow + ((pressed & PadButton.Up) != 0 ? -1 : 1), 0, lastRow);
                if (target != padKeyRow)
                {
                    padKeyCol = MapKeyColumn(padKeyRow, target, padKeyCol, lastRow);
                    padKeyRow = target;
                    moved = true;
                }
            }
            if ((pressed & (PadButton.Left | PadButton.Right)) != 0)
            {
                int count = padKeyCells[padKeyRow].Count;
                padKeyCol = (padKeyCol + ((pressed & PadButton.Left) != 0 ? -1 : 1) + count) % count;
                moved = true;
            }
            if (moved)
            {
                UpdatePadKeyboard();
                PlayUiSound("move");
            }

            if ((pressed & PadButton.Confirm) != 0)
            {
                if (padKeyRow < lastRow) TypePadKey(PadKeyRows[padKeyRow][padKeyCol].ToString());
                else if (padKeyCol == 0) TypePadKey(" ");
                else if (padKeyCol == 1) DeletePadKey();
                else ClosePadKeyboard();
            }
            // wie auf der PlayStation: □ löscht, △ Leerzeichen, R2 bestätigt (Fertig), ○ schließt
            if ((pressed & PadButton.Details) != 0) DeletePadKey();
            if ((pressed & PadButton.Favorite) != 0) TypePadKey(" ");
            if ((pressed & PadButton.Back) != 0) ClosePadKeyboard();
            if ((pressed & (PadButton.Start | PadButton.TriggerRight)) != 0) ClosePadKeyboard();
        }

        private void TypePadKey(string text)
        {
            if (padSearch.Length >= 40 || (text == " " && (padSearch.Length == 0 || padSearch.EndsWith(' ')))) return;
            padSearch += padSearch.Length == 0 ? text : text.ToLower(CultureInfo.CurrentCulture);
            PlayUiSound("select");
            UpdatePadKeyboard();
            RebuildPadItems();
        }

        private void DeletePadKey()
        {
            if (padSearch.Length == 0) return;
            padSearch = padSearch.Substring(0, padSearch.Length - 1);
            UpdatePadKeyboard();
            RebuildPadItems();
        }

        private void ClearPadSearch()
        {
            if (padSearch.Length == 0) return;
            padSearch = string.Empty;
            RebuildPadItems();
            RefreshPadTabs();
        }

        /// <summary>Treffer für die Controller-Suche: Spiele und Anwendungen, deren Name den Suchtext enthält.</summary>
        private IEnumerable<PadItem> SearchPadItems(string query)
        {
            string text = query.Trim();
            var games = allGames.Where(g => !g.Hidden && g.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(g => !g.Name.StartsWith(text, StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(PadItemFromGame);
            var apps = cachedApps.Where(a => a.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(PadItemFromApp);
            return games.Concat(apps);
        }

        // ───────── Lautstärke (Windows-Hauptlautstärke) ─────────

        private bool padVolumeOpen;
        private Grid? padVolumeHost;
        private TextBlock? padVolumeText, padVolumeHint;
        private Border? padVolumeFill;
        private int padVolume = -1;
        private bool padVolumeMuted;
        private readonly object padVolumeLock = new();

        private void EnsurePadVolume()
        {
            if (padVolumeHost != null || padLayer == null) return;

            padVolumeText = new TextBlock { FontSize = 44, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            padVolumeFill = new Border { CornerRadius = new CornerRadius(5), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Width = 0 };
            padVolumeFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            var bar = new Border { Width = 400, Height = 10, CornerRadius = new CornerRadius(5), Background = MakeBrush("#26FFFFFF"), Margin = new Thickness(0, 14, 0, 16), Child = padVolumeFill };
            var hint = padVolumeHint = new TextBlock
            {
                FontSize = 15,
                Foreground = MakeBrush("#9CA3AF"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            var card = new Border
            {
                Width = 480,
                Padding = new Thickness(28),
                CornerRadius = new CornerRadius(22),
                Background = MakeBrush("#F2131722"),
                BorderBrush = BrushCardBorder,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = Loc.T("🔊 Lautstärke"), FontSize = 22, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) },
                        padVolumeText, bar, hint
                    }
                }
            };

            padVolumeHost = new Grid { Visibility = Visibility.Collapsed, Background = MakeBrush("#B3000000") };
            padVolumeHost.Children.Add(card);
            padLayer.Children.Add(padVolumeHost);
        }

        private async void OpenPadVolume()
        {
            EnsurePadVolume();
            if (padVolumeHost == null) return;

            var current = await Task.Run(SystemVolume.Read);
            if (current == null)
            {
                ShowToast("🔊", "Lautstärke", "Die Windows-Lautstärke konnte nicht gelesen werden.", 4);
                return;
            }

            padVolume = (int)Math.Round(current.Value.Level * 100);
            padVolumeMuted = current.Value.Muted;
            if (padVolumeHint != null)
                padVolumeHint.Text = $"◀ ▶  {Loc.T("ändern")}   ·   {PadGlyph("favorite")} {Loc.T("stumm")}   ·   {PadGlyph("back")} {Loc.T("Schließen")}";
            padVolumeOpen = true;
            padVolumeHost.Visibility = Visibility.Visible;
            UpdatePadVolume();
        }

        private void ClosePadVolume()
        {
            padVolumeOpen = false;
            if (padVolumeHost != null) padVolumeHost.Visibility = Visibility.Collapsed;
        }

        private void UpdatePadVolume()
        {
            if (padVolumeText == null || padVolumeFill == null) return;
            padVolumeText.Text = padVolumeMuted ? "🔇  " + Loc.T("Stumm") : $"{padVolume} %";
            padVolumeFill.Width = 400 * Math.Clamp(padVolume, 0, 100) / 100.0;
            padVolumeFill.Opacity = padVolumeMuted ? 0.35 : 1;
        }

        private void HandlePadVolume(PadButton pressed)
        {
            int delta = 0;
            if ((pressed & (PadButton.Right | PadButton.Up)) != 0) delta = 5;
            if ((pressed & (PadButton.Left | PadButton.Down)) != 0) delta = -5;

            if (delta != 0)
            {
                padVolume = Math.Clamp(padVolume + delta, 0, 100);
                padVolumeMuted = false;
                ApplyPadVolume();
                PlayUiSound("move");
            }
            if ((pressed & PadButton.Favorite) != 0)
            {
                padVolumeMuted = !padVolumeMuted;
                ApplyPadVolume();
            }
            if ((pressed & (PadButton.Back | PadButton.Confirm | PadButton.Start)) != 0) ClosePadVolume();
        }

        /// <summary>Setzt die Lautstärke im Hintergrund; es gilt immer der zuletzt gewählte Wert.</summary>
        private void ApplyPadVolume()
        {
            UpdatePadVolume();
            float level = padVolume / 100f;
            bool muted = padVolumeMuted;
            _ = Task.Run(() =>
            {
                lock (padVolumeLock) SystemVolume.Write(level, muted);
            });
        }

        // ───────── Spiel beenden, Neustart und Herunterfahren ─────────

        /// <summary>Bestätigung im Controller-Menü: „Abbrechen“ steht oben, damit nichts aus Versehen passiert.</summary>
        private void OpenPadConfirm(string title, string confirmLabel, Action action)
        {
            OpenPadMenu(title, new List<(string, Action)>
            {
                (Loc.T("Abbrechen"), () => { }),
                (confirmLabel, action)
            });
        }

        private void PadEndGame()
        {
            var running = activeSessions.Keys.ToList();
            if (running.Count == 0) return;

            void Confirm(GameItem game) => OpenPadConfirm(Loc.T($"„{game.Name}“ beenden? Nicht gespeicherter Fortschritt geht verloren."),
                Loc.T("⏹  Spiel beenden"), () => _ = EndGameAsync(game));

            if (running.Count == 1)
            {
                Confirm(running[0]);
                return;
            }

            var entries = running.Select(g => (g.Name, (Action)(() => Confirm(g)))).ToList();
            entries.Add((Loc.T("Abbrechen"), () => { }));
            OpenPadMenu(Loc.T("Welches Spiel beenden?"), entries);
        }

        /// <summary>Beendet ein laufendes Spiel: erst normal schließen, nach 5 Sekunden notfalls hart beenden.</summary>
        private async Task EndGameAsync(GameItem game)
        {
            string installDir = game.InstallDir;
            string exe = game.ExecutablePath;

            bool Matches(string path)
            {
                // Nur eindeutige Spielordner, nie ein ganzes Laufwerk oder „Programme“
                if (installDir.Length > 0)
                {
                    string dir = installDir.TrimEnd('\\', '/');
                    string root = System.IO.Path.GetPathRoot(dir) ?? string.Empty;
                    int depth = dir.Substring(Math.Min(root.Length, dir.Length)).Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
                    string last = System.IO.Path.GetFileName(dir);
                    string[] generic = { "common", "steamapps", "Games", "Program Files", "Program Files (x86)", "Epic Games", "SteamLibrary", "GOG Games", "XboxGames" };
                    bool specific = depth >= 2 && !generic.Contains(last, StringComparer.OrdinalIgnoreCase);
                    if (specific && path.StartsWith(dir + "\\", StringComparison.OrdinalIgnoreCase)) return true;
                }
                return exe.Length > 0 && string.Equals(path, exe, StringComparison.OrdinalIgnoreCase);
            }

            int Close(bool force)
            {
                int count = 0;
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId) continue;
                        string? path = NativeFeatures.GetProcessPath(process.Id);
                        if (string.IsNullOrEmpty(path) || !Matches(path)) continue;
                        count++;
                        if (force) process.Kill(true);
                        else process.CloseMainWindow();
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                return count;
            }

            ShowToast("⏹", "Spiel wird beendet", game.Name, 4, null, true);
            int found = await Task.Run(() => Close(false));
            if (found == 0)
            {
                ShowToast("⏹", "Spiel beenden", Loc.T("Zu diesem Spiel läuft kein Programm mehr."), 4, null, true);
                return;
            }

            await Task.Delay(5000);
            int remaining = await Task.Run(() => Close(true));
            if (remaining > 0) LogError("Spiel beenden", new InvalidOperationException($"{game.Name}: {remaining} Prozess(e) hart beendet"));
        }

        private void PadPowerAction(string kind)
        {
            string title = kind == "restart"
                ? Loc.T("PC jetzt neu starten?")
                : Loc.T("PC jetzt herunterfahren?");
            if (activeSessions.Count > 0) title += " " + Loc.T("Es läuft noch ein Spiel.");

            OpenPadConfirm(title, Loc.T(kind == "restart" ? "🔄  Neu starten" : "⏻  Herunterfahren"), () =>
            {
                try
                {
                    SaveSettings();
                    Process.Start(new ProcessStartInfo("shutdown.exe", kind == "restart" ? "/r /t 3" : "/s /t 3") { UseShellExecute = false, CreateNoWindow = true });
                }
                catch (Exception ex)
                {
                    LogError("Energie", ex);
                    ShowToast("⚠", "Energie", Loc.T("Das hat nicht geklappt."), 5, null, true);
                }
            });
        }
    }
}
