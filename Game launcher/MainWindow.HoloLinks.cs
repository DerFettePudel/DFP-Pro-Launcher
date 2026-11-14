using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Schnellzugriff ─────────────────────────────
        //
        // Verknüpfungen als „Portale“: Symbol in einem Ring mit gestricheltem Außenrand, nach Art gruppiert
        // (Webseiten, Programme, Ordner und Dateien). Rechtsklick: Bearbeiten, Verschieben, Entfernen wie in Thema 1.

        private FrameworkElement BuildHoloLinks()
        {
            SeedQuickLinks();
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Schnellzugriff"), Loc.T("Schnellzugriff"),
                Loc.T("Deine eigenen Verknüpfungen zu Webseiten, Programmen und Ordnern. Rechtsklick auf eine Kachel zum Bearbeiten, Verschieben oder Entfernen."),
                HoloButton("Hinzufügen", () => BtnAddQuick_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Standard wiederherstellen", () => BtnResetQuick_Click(this, new RoutedEventArgs()), false, "")));

            var links = settings.QuickLinks.ToList();
            if (links.Count == 0)
            {
                page.Children.Add(HoloPanel(Loc.T("Schnellzugriff"), HoloHint("Noch keine Verknüpfungen. Füge mit „Hinzufügen“ Webseiten, Programme oder Ordner hinzu.")));
                return page;
            }

            static string Kind(string target)
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return "web";
                if (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) return "app";
                return "other";
            }

            var groups = new[]
            {
                ("web", Loc.T("Webseiten"), ""),
                ("app", Loc.T("Programme"), ""),
                ("other", Loc.T("Ordner und Dateien"), "")
            };

            // Zwei Spalten: Webseiten breit links, der Rest rechts (bei wenigen Einträgen untereinander)
            var left = new StackPanel();
            var right = new StackPanel();
            foreach (var (key, title, glyph) in groups)
            {
                var items = links.Where(l => Kind(l.Target) == key).ToList();
                if (items.Count == 0) continue;
                var wrap = new WrapPanel();
                foreach (var link in items) wrap.Children.Add(HoloPortal(link));
                var panel = HoloPanel(title + "  " + items.Count.ToString(Loc.Culture), wrap);
                (key == "web" ? left : right).Children.Add(panel);
            }
            if (right.Children.Count == 0) page.Children.Add(left);
            else if (left.Children.Count == 0) page.Children.Add(right);
            else page.Children.Add(HoloColumns(left, right, 0.38));
            return page;
        }

        private FrameworkElement HoloPortal(QuickLink link)
        {
            var item = link;
            const double size = 96;
            var accent = GetAccentColor();

            var ring = new Grid { Width = size, Height = size, HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            var dashes = new System.Windows.Shapes.Ellipse
            {
                Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, HoloCyan.R, HoloCyan.G, HoloCyan.B)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 2, 4 },
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new RotateTransform(0)
            };
            ring.Children.Add(dashes);
            var disc = new System.Windows.Shapes.Ellipse { Margin = new Thickness(8), Fill = MakeBrush("#CC0E1628"), Stroke = MakeBrush("#33A5B4D0"), StrokeThickness = 1.5 };
            ring.Children.Add(disc);

            if (ResolveQuickIcon(item) is ImageSource source)
            {
                var image = new System.Windows.Controls.Image { Source = source, Width = 38, Height = 38 };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                ring.Children.Add(image);
            }
            else ring.Children.Add(new TextBlock { Text = QuickEmoji(item), FontSize = 30, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center });

            var name = new TextBlock { Text = item.Name, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0) };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            var target = new TextBlock { Text = DescribeTarget(item.Target), FontSize = 10.5, Foreground = ConsoleMuted, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            target.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");

            var menu = CreateMenu();
            AddMenuItem(menu, "✏  Bearbeiten ...", () => EditQuickLink(item));
            AddMenuItem(menu, "◀  Nach vorn", () => MoveQuickLink(item, -1));
            AddMenuItem(menu, "▶  Nach hinten", () => MoveQuickLink(item, 1));
            AddMenuItem(menu, "🗑  Entfernen", () => RemoveQuickLink(item));

            var tile = new StackPanel { Width = 136, Margin = new Thickness(0, 0, 10, 14), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent, ContextMenu = menu, ToolTip = item.Target };
            tile.Children.Add(ring);
            tile.Children.Add(name);
            tile.Children.Add(target);

            tile.MouseEnter += (s, e) =>
            {
                disc.Stroke = new SolidColorBrush(accent);
                disc.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, accent.R, accent.G, accent.B));
                if (!settings.PerformanceMode) disc.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accent, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.8 };
                dashes.Stroke = new SolidColorBrush(HoloCyan);
                name.Foreground = ConsoleCyan;
                if (settings.HoverAnimations)
                    ((RotateTransform)dashes.RenderTransform).BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 90, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
            tile.MouseLeave += (s, e) =>
            {
                disc.Stroke = MakeBrush("#33A5B4D0");
                disc.Fill = MakeBrush("#CC0E1628");
                disc.Effect = null;
                dashes.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, HoloCyan.R, HoloCyan.G, HoloCyan.B));
                name.Foreground = System.Windows.Media.Brushes.White;
            };
            tile.MouseLeftButtonUp += (s, e) => OpenShell(item.Target);
            return tile;
        }
    }
}
