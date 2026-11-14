using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Wunschliste („Preis-Radar“) ─────────────────────────────
        //
        // Zahlen-Platten oben, darunter jedes Spiel als Karte mit großem Shop-Bild, schrägem Rabatt-Schild,
        // großem Preis und einer Preisleiste (Normalpreis → aktueller Preis, Marke beim Zielpreis).

        private FrameworkElement BuildHoloWishlist()
        {
            var page = new StackPanel();
            var list = settings.Wishlist.ToList();
            var last = list.Count > 0 ? list.Max(w => w.Checked) : DateTime.MinValue;
            string status = last > DateTime.MinValue ? Loc.T("Zuletzt geprüft:") + " " + last.ToString("HH:mm", Loc.Culture) : string.Empty;

            page.Children.Add(HoloHeader(Loc.T("Wunschliste"), Loc.T("Wunschliste"),
                Loc.T("Trage Steam-Spiele ein, die du gern hättest. Der Launcher prüft die Preise und meldet sich, sobald dein Zielpreis erreicht ist."),
                HoloButton("Hinzufügen", () => BtnWishAdd_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Preise prüfen", () => BtnWishRefresh_Click(this, new RoutedEventArgs()), false, "")));

            if (SHide(settings.StreamHideShots))
            {
                page.Children.Add(HoloPanel(Loc.T("Streamer-Modus"), HoloHint("Im Streamer-Modus ausgeblendet.")));
                return page;
            }
            if (list.Count == 0)
            {
                page.Children.Add(HoloPanel(Loc.T("Wunschliste"), HoloHint("Noch keine Spiele auf der Wunschliste. Füge ein Steam-Spiel hinzu, der Launcher meldet sich, sobald der Preis fällt.")));
                return page;
            }

            var stats = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(-6, 0, 4, 4) };
            stats.Children.Add(HoloStat(Loc.T("Auf der Liste"), list.Count.ToString(Loc.Culture), null, list.Count));
            int reachedCount = list.Count(WishTargetReached);
            stats.Children.Add(HoloStat(Loc.T("Ziel erreicht"), reachedCount.ToString(Loc.Culture), null, reachedCount));
            int best = list.Max(w => w.Discount);
            stats.Children.Add(HoloStat(Loc.T("Höchster Rabatt"), best > 0 ? "-" + best.ToString(Loc.Culture) + " %" : "—"));
            stats.Children.Add(HoloStat(Loc.T("Zuletzt geprüft"), last > DateTime.MinValue ? last.ToString("HH:mm", Loc.Culture) : "—"));
            page.Children.Add(stats);

            var grid = HoloCardGrid(420);
            foreach (var wish in list.OrderByDescending(WishTargetReached).ThenByDescending(w => w.Discount).ThenBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase))
                grid.Children.Add(HoloWishCard(wish));
            page.Children.Add(grid);
            if (status.Length > 0)
            {
                var foot = HoloHint(status);
                foot.Margin = new Thickness(2, 0, 0, 0);
                page.Children.Add(foot);
            }
            return page;
        }

        private FrameworkElement HoloWishCard(WishItem item)
        {
            bool reached = WishTargetReached(item);
            var accent = GetAccentColor();
            string store = $"https://store.steampowered.com/app/{item.AppId}";

            var body = new StackPanel { Margin = new Thickness(0) };

            // ── Bild mit Rabatt-Schild ──
            var picture = new Grid { Height = 170, ClipToBounds = true };
            var header = LoadWebImage($"https://cdn.akamai.steamstatic.com/steam/apps/{item.AppId}/header.jpg");
            picture.Background = header != null ? new ImageBrush(header) { Stretch = Stretch.UniformToFill } : MakeBrush("#141C2E");
            var shade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x07, 0x0B, 0x14), 0.45));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF0, 0x07, 0x0B, 0x14), 1));
            picture.Children.Add(new Border { Background = shade });

            if (item.Discount > 0)
            {
                var tag = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(0, 14, 16, 0) };
                var plate = new Border { RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new SkewTransform(-14, 0) };
                plate.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                if (!settings.PerformanceMode) plate.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
                tag.Children.Add(plate);
                var text = new TextBlock { Text = "-" + item.Discount.ToString(Loc.Culture) + "%", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = MakeBrush(InkFor(accent)), Margin = new Thickness(14, 4, 14, 4) };
                text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                tag.Children.Add(text);
                picture.Children.Add(tag);
            }

            var titleRow = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Margin = new Thickness(20, 0, 20, 10) };
            if (reached) titleRow.Children.Add(HoloLabel("🔔 " + Loc.T("Ziel erreicht")));
            var name = ConsoleTitle(item.Name, 22);
            titleRow.Children.Add(name);
            picture.Children.Add(titleRow);
            body.Children.Add(picture);

            // ── Preis und Leiste ──
            var info = new StackPanel { Margin = new Thickness(20, 12, 20, 18) };
            var priceRow = new Grid();
            priceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            priceRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var price = ConsoleTitle(string.IsNullOrEmpty(item.PriceText) ? "–" : Loc.T(item.PriceText), 30);
            if (reached) price.Foreground = MakeBrush("#34D399");
            priceRow.Children.Add(price);
            var target = new TextBlock
            {
                Text = item.TargetPrice > 0 ? Loc.T("Zielpreis:") + " " + FormatMoney(item.TargetPrice) : Loc.T("Ziel: ab 20 % Rabatt"),
                FontSize = 12,
                Foreground = ConsoleMuted,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            target.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            Grid.SetColumn(target, 1);
            priceRow.Children.Add(target);
            info.Children.Add(priceRow);

            if (item.InitialPrice > 0 && item.FinalPrice > 0)
            {
                double share = Math.Clamp(item.FinalPrice / item.InitialPrice, 0, 1);
                double mark = item.TargetPrice > 0 ? Math.Clamp(item.TargetPrice / item.InitialPrice, 0, 1) : 0.8;
                var meter = new Grid { Height = 10, Margin = new Thickness(0, 12, 0, 4) };
                meter.Children.Add(new Border { Background = MakeBrush("#18223A"), RenderTransform = new SkewTransform(-20, 0) });
                var bar = new Border { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Background = reached ? MakeBrush("#34D399") : new SolidColorBrush(HoloCyan), Opacity = 0.9 };
                var grow = new ScaleTransform(1, 1);
                bar.RenderTransform = new TransformGroup { Children = { grow, new SkewTransform(-20, 0) } };
                var marker = new Border { Width = 3, Margin = new Thickness(0, -4, 0, -4), HorizontalAlignment = System.Windows.HorizontalAlignment.Left, ToolTip = item.TargetPrice > 0 ? Loc.T("Zielpreis:") + " " + FormatMoney(item.TargetPrice) : Loc.T("Ziel: ab 20 % Rabatt") };
                marker.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                meter.SizeChanged += (s, e) =>
                {
                    bar.Width = e.NewSize.Width * share;
                    marker.Margin = new Thickness(e.NewSize.Width * mark - 1.5, -4, 0, -4);
                };
                if (holoAnimate) grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(400), EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut } });
                meter.Children.Add(bar);
                meter.Children.Add(marker);
                info.Children.Add(meter);

                var scale = new Grid();
                scale.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                scale.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var zero = new TextBlock { Text = "0", FontSize = 10, Foreground = ConsoleMuted };
                zero.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                scale.Children.Add(zero);
                var full = new TextBlock { Text = FormatMoney(item.InitialPrice), FontSize = 10, Foreground = ConsoleMuted };
                full.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                Grid.SetColumn(full, 1);
                scale.Children.Add(full);
                info.Children.Add(scale);
            }
            body.Children.Add(info);

            var menu = CreateMenu();
            AddMenuItem(menu, "🛒  Im Steam-Shop öffnen", () => OpenShell(store));
            AddMenuItem(menu, "🎯  Zielpreis ändern ...", () => EditWishTarget(item));
            AddMenuItem(menu, "🗑  Entfernen", () =>
            {
                settings.Wishlist.Remove(item);
                SaveSettings();
                RenderWishlist();
            });

            var plateHost = HoloPlate(body, 22, out var edge);
            if (reached) edge.Stroke = new SolidColorBrush(MakeColor("#34D399"));
            plateHost.Margin = new Thickness(0, 0, 18, 18);
            plateHost.Cursor = System.Windows.Input.Cursors.Hand;
            plateHost.ContextMenu = menu;
            plateHost.ToolTip = Loc.T("Im Steam-Shop öffnen");
            plateHost.MouseEnter += (s, e) => { edge.Stroke = HoloEdgeBrush(0xFF); edge.StrokeThickness = 2; };
            plateHost.MouseLeave += (s, e) => { edge.Stroke = reached ? new SolidColorBrush(MakeColor("#34D399")) : HoloEdgeBrush(); edge.StrokeThickness = 1; };
            plateHost.MouseLeftButtonUp += (s, e) => OpenShell(store);
            HoloEnter(plateHost, edge, plateHost);
            return plateHost;
        }
    }
}
