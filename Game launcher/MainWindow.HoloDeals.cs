using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Angebote („Beute-Terminal“) ─────────────────────────────
        //
        // Das erste Gratis-Spiel als großes Banner mit Stempel „GRATIS“, weitere Gratis-Spiele daneben, „Demnächst“ als
        // Leiste und Steam-Rabatte als Karten mit großem schrägem Rabatt-Schild.

        private FrameworkElement BuildHoloDeals()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Angebote"), Loc.T("Angebote"), Loc.T("Gratis-Spiele bei Epic Games und aktuelle Rabatte bei Steam."),
                HoloButton("Aktualisieren", () => BtnRefreshDeals_Click(this, new RoutedEventArgs()), true, "")));

            if (!settings.OnlineFeatures)
            {
                page.Children.Add(HoloPanel(Loc.T("Angebote"), HoloHint("Online-Funktionen sind in den Einstellungen deaktiviert.")));
                return page;
            }
            if (TxtDealsStatus?.Text is { Length: > 0 } status)
            {
                var line = HoloHint(status);
                line.Margin = new Thickness(2, -12, 0, 14);
                page.Children.Add(line);
            }

            // ── Gratis jetzt ──
            if (epicFree.Count > 0)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(HoloDealBanner(epicFree[0]));
                if (epicFree.Count > 1)
                {
                    var more = new StackPanel();
                    foreach (var deal in epicFree.Skip(1).Take(3)) more.Children.Add(HoloDealCard(deal, 150));
                    Grid.SetColumn(more, 1);
                    row.Children.Add(more);
                }
                else Grid.SetColumnSpan(row.Children[0], 2);
                page.Children.Add(row);
            }
            else page.Children.Add(HoloPanel(Loc.T("Epic Games Store – jetzt gratis"), HoloHint("Zurzeit gibt es keine Gratis-Spiele.")));

            // ── Demnächst ──
            var upcoming = HoloCardGrid(300);
            foreach (var deal in epicUpcoming.Take(8)) upcoming.Children.Add(HoloDealCard(deal, 140, true));
            page.Children.Add(HoloPanel(Loc.T("Epic Games Store – demnächst gratis"), epicUpcoming.Count > 0 ? upcoming : HoloHint("Noch keine Vorschau verfügbar.")));

            // ── Steam-Rabatte ──
            var steam = HoloCardGrid(300);
            foreach (var deal in steamDeals) steam.Children.Add(HoloDealCard(deal, 140));
            page.Children.Add(HoloPanel(Loc.T("Steam – aktuelle Rabatte"), steamDeals.Count > 0 ? steam : HoloHint("Keine Angebote gefunden.")));
            return page;
        }

        /// <summary>Großes Banner: Bild über die ganze Fläche, Stempel „GRATIS“, Titel groß, Knopf zum Store.</summary>
        private FrameworkElement HoloDealBanner(DealItem deal)
        {
            var body = new Grid { Height = 360 };
            var picture = LoadWebImage(deal.ImageUrl);
            if (picture != null) body.Children.Add(new System.Windows.Controls.Image { Source = picture, Stretch = Stretch.UniformToFill, Margin = new Thickness(-22, -16, -22, -20) });
            var shade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x06, 0x09, 0x10), 0.3));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xF2, 0x06, 0x09, 0x10), 1));
            body.Children.Add(new Border { Background = shade, Margin = new Thickness(-22, -16, -22, -20) });

            var stamp = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top, RenderTransformOrigin = new System.Windows.Point(0.5, 0.5), RenderTransform = new RotateTransform(-6) };
            var stampPlate = new Border { BorderThickness = new Thickness(2), Background = MakeBrush("#CC06090F"), RenderTransform = new SkewTransform(-12, 0) };
            stampPlate.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            if (!settings.PerformanceMode) stampPlate.SetResourceReference(UIElement.EffectProperty, "NeonGlow");
            stamp.Children.Add(stampPlate);
            var stampText = new TextBlock { Text = Loc.T("Gratis").ToUpper(Loc.Culture), FontSize = 26, FontWeight = FontWeights.Bold, Margin = new Thickness(18, 4, 18, 4) };
            stampText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            stampText.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            stamp.Children.Add(stampText);
            body.Children.Add(stamp);

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            info.Children.Add(HoloLabel(deal.Subtitle));
            var title = ConsoleTitle(deal.Title, 44);
            title.TextWrapping = TextWrapping.Wrap;
            title.Margin = new Thickness(0, 4, 0, 12);
            info.Children.Add(title);
            var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, -10) };
            buttons.Children.Add(HoloButton("Im Store holen", () => OpenShell(deal.Url), true, ""));
            info.Children.Add(buttons);
            body.Children.Add(info);

            var panel = HoloPanel(Loc.T("Epic Games Store – jetzt gratis"), body, null, 28);
            panel.Cursor = System.Windows.Input.Cursors.Hand;
            panel.MouseLeftButtonUp += (s, e) => OpenShell(deal.Url);
            return panel;
        }

        /// <summary>Karte: Bild, schräges Schild (Rabatt oder Datum), Titel, Untertitel, Store-Logo.</summary>
        private FrameworkElement HoloDealCard(DealItem deal, double imageHeight, bool upcoming = false)
        {
            var accent = GetAccentColor();
            var body = new StackPanel();
            var picture = new Grid { Height = imageHeight, ClipToBounds = true, Background = MakeBrush("#141C2E") };
            var image = LoadWebImage(deal.ImageUrl);
            if (image != null) picture.Children.Add(new System.Windows.Controls.Image { Source = image, Stretch = Stretch.UniformToFill, Opacity = upcoming ? 0.6 : 1 });

            if (deal.Badge.Length > 0)
            {
                var tag = new Grid { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(0, 10, 14, 0) };
                var plate = new Border { RenderTransform = new SkewTransform(-14, 0) };
                if (upcoming) { plate.Background = MakeBrush("#E60B1222"); plate.BorderBrush = new SolidColorBrush(HoloCyan); plate.BorderThickness = new Thickness(1); }
                else plate.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                tag.Children.Add(plate);
                var text = new TextBlock { Text = deal.Badge, FontSize = upcoming ? 12 : 18, FontWeight = FontWeights.Bold, Foreground = upcoming ? ConsoleCyan : MakeBrush(InkFor(accent)), Margin = new Thickness(12, 3, 12, 3) };
                text.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
                tag.Children.Add(text);
                picture.Children.Add(tag);
            }
            body.Children.Add(picture);

            var texts = new StackPanel { Margin = new Thickness(16, 10, 16, 14) };
            var title = new TextBlock { Text = deal.Title, FontSize = 15, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            texts.Children.Add(title);
            var sub = new TextBlock { Text = deal.Subtitle, FontSize = 12, Foreground = ConsoleMuted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            texts.Children.Add(sub);
            if (GetStoreLogo(deal.Store) is ImageSource logo)
            {
                var store = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
                store.Children.Add(new System.Windows.Controls.Image { Source = logo, Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0) });
                var name = HoloLabel(deal.Store == "steam" ? "Steam" : "Epic Games Store", 10);
                name.Foreground = ConsoleMuted;
                store.Children.Add(name);
                texts.Children.Add(store);
            }
            body.Children.Add(texts);

            var plateHost = HoloPlate(body, 16, out var edge);
            plateHost.Margin = new Thickness(0, 0, 16, 16);
            plateHost.Cursor = System.Windows.Input.Cursors.Hand;
            plateHost.ToolTip = deal.Title;
            plateHost.MouseEnter += (s, e) => edge.Stroke = HoloEdgeBrush(0xFF);
            plateHost.MouseLeave += (s, e) => edge.Stroke = HoloEdgeBrush();
            plateHost.MouseLeftButtonUp += (s, e) => OpenShell(deal.Url);
            HoloEnter(plateHost);
            return plateHost;
        }
    }
}
