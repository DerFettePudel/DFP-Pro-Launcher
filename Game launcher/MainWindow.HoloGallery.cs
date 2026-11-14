using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Galerie („Bild-Archiv“) ─────────────────────────────
        //
        // Das neueste Bild groß als Titelbild, links eine Schiene nach Spiel/Quelle, rechts ein Raster mit Vorschaubildern.
        // Ein Klick öffnet einen eigenen Bildbetrachter auf der Holo-Ebene (blättern mit Pfeilen, Esc schließt).

        private List<ShotInfo>? lastShots;
        private string holoShotFilter = string.Empty;
        private readonly Dictionary<string, BitmapImage> holoThumbs = new(StringComparer.OrdinalIgnoreCase);
        private Grid? holoLightbox;

        private FrameworkElement BuildHoloGallery()
        {
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Galerie"), Loc.T("Galerie"), Loc.T("Deine neuesten Screenshots aus Windows, Xbox Game Bar und Steam."),
                HoloButton("Aktualisieren", () => BtnRefreshGallery_Click(this, new RoutedEventArgs()), true, ""),
                HoloButton("Ordner hinzufügen", () => BtnAddShotFolder_Click(this, new RoutedEventArgs()), false, "")));

            if (SHide(settings.StreamHideShots))
            {
                page.Children.Add(HoloPanel(Loc.T("Streamer-Modus"), HoloHint("Im Streamer-Modus ausgeblendet.")));
                return page;
            }
            if (lastShots == null)
            {
                _ = RefreshGalleryAsync();
                page.Children.Add(HoloPanel(Loc.T("Galerie"), HoloHint("Screenshots werden gesucht ...")));
                return page;
            }

            var shots = lastShots.Take(240).ToList();
            if (shots.Count == 0)
            {
                page.Children.Add(HoloPanel(Loc.T("Galerie"), HoloHint("Keine Screenshots gefunden. Füge über „Ordner hinzufügen“ deinen Screenshot-Ordner hinzu.")));
                return page;
            }

            // ── Titelbild ──
            var latest = shots[0];
            var hero = new Grid { Height = 380, Cursor = System.Windows.Input.Cursors.Hand };
            var heroImage = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill, Margin = new Thickness(-22, -16, -22, -20) };
            hero.Children.Add(heroImage);
            _ = LoadHoloImageAsync(latest.FilePath, heroImage, 1600);
            var shade = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0x00, 0x06, 0x09, 0x10), 0.5));
            shade.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb(0xE6, 0x06, 0x09, 0x10), 1));
            hero.Children.Add(new Border { Background = shade, Margin = heroImage.Margin });
            var heroText = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom };
            heroText.Children.Add(HoloLabel(Loc.T("Neuester Screenshot") + "  ·  " + latest.Time.ToString("g", Loc.Culture)));
            var heroTitle = ConsoleTitle(latest.Label, 40);
            heroText.Children.Add(heroTitle);
            hero.Children.Add(heroText);
            hero.MouseLeftButtonUp += (s, e) => OpenHoloLightbox(shots, 0);
            page.Children.Add(HoloPanel(Loc.T("Titelbild"), hero, null, 28));

            // ── Schiene und Raster ──
            var labels = shots.GroupBy(s => s.Label).OrderByDescending(g => g.Count()).ToList();
            if (holoShotFilter.Length > 0 && !labels.Any(g => g.Key == holoShotFilter)) holoShotFilter = string.Empty;

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(layout);

            var rail = new StackPanel();
            rail.Children.Add(HoloRailItem(Loc.T("Alle"), shots.Count.ToString(Loc.Culture), holoShotFilter.Length == 0, () => { holoShotFilter = string.Empty; RefreshHoloPage(); }, ""));
            foreach (var group in labels.Take(20))
            {
                string key = group.Key;
                rail.Children.Add(HoloRailItem(key, group.Count().ToString(Loc.Culture), holoShotFilter == key, () => { holoShotFilter = key; RefreshHoloPage(); }, ""));
            }
            layout.Children.Add(HoloPanel(Loc.T("Spiele"), rail));

            var filtered = shots.Where(s => holoShotFilter.Length == 0 || s.Label == holoShotFilter).Take(90).ToList();
            var grid = new WrapPanel();
            HoloFitWrap(grid, 250);
            for (int i = 0; i < filtered.Count; i++) grid.Children.Add(HoloShotTile(filtered, i));
            var content = HoloPanel((holoShotFilter.Length == 0 ? Loc.T("Alle") : holoShotFilter) + "  " + filtered.Count.ToString(Loc.Culture), grid);
            Grid.SetColumn(content, 1);
            layout.Children.Add(content);
            return page;
        }

        private async Task LoadHoloImageAsync(string path, System.Windows.Controls.Image target, int width = 380)
        {
            string key = path + "|" + width;
            if (holoThumbs.TryGetValue(key, out var cached)) { target.Source = cached; return; }
            var image = await Task.Run(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(path);
                    bitmap.DecodePixelWidth = width;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch { return null; }
            });
            if (image == null) return;
            if (holoThumbs.Count > 200) holoThumbs.Clear();
            holoThumbs[key] = image;
            target.Source = image;
            if (settings.PageAnimations) target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }

        private FrameworkElement HoloShotTile(List<ShotInfo> list, int index)
        {
            var shot = list[index];
            var tile = new Grid { Height = 150, Margin = new Thickness(0, 0, 12, 12), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = shot.Label + "  ·  " + shot.Time.ToString("g", Loc.Culture), Background = MakeBrush("#141C2E") };
            var image = new System.Windows.Controls.Image { Stretch = Stretch.UniformToFill };
            tile.Children.Add(image);
            _ = LoadHoloImageAsync(shot.FilePath, image);

            var edge = new System.Windows.Shapes.Path { Stroke = MakeBrush("#33A5B4D0"), StrokeThickness = 1 };
            tile.Children.Add(HoloShapeLayer(edge));
            var caption = new Border { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, Background = MakeBrush("#B306090F"), Padding = new Thickness(10, 5, 10, 6), Opacity = 0 };
            var text = new TextBlock { Text = shot.Time.ToString("g", Loc.Culture), FontSize = 11, Foreground = ConsoleCyan };
            text.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
            caption.Child = text;
            tile.Children.Add(caption);
            tile.SizeChanged += (s, e) =>
            {
                var geometry = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 14);
                tile.Clip = geometry;
                edge.Data = geometry;
            };

            var menu = CreateMenu();
            AddMenuItem(menu, "🖼  Öffnen", () => OpenShell(shot.FilePath));
            AddMenuItem(menu, "📂  Im Ordner zeigen", () => OpenShell("explorer.exe", $"/select,\"{shot.FilePath}\""));
            AddMenuItem(menu, "🎨  Als Hintergrundbild verwenden", () => SetBackgroundFromFile(shot.FilePath));
            tile.ContextMenu = menu;

            tile.MouseEnter += (s, e) => { edge.Stroke = HoloEdgeBrush(0xFF); edge.StrokeThickness = 2; caption.Opacity = 1; };
            tile.MouseLeave += (s, e) => { edge.Stroke = MakeBrush("#33A5B4D0"); edge.StrokeThickness = 1; caption.Opacity = 0; };
            tile.MouseLeftButtonUp += (s, e) => OpenHoloLightbox(list, index);
            return tile;
        }

        // ───────── Bildbetrachter ─────────

        private void OpenHoloLightbox(List<ShotInfo> list, int index)
        {
            if (holoHost == null || list.Count == 0) return;
            CloseHoloLightbox();

            var box = new Grid { Background = MakeBrush("#F206080D"), Focusable = true };
            System.Windows.Controls.Panel.SetZIndex(box, 50);
            var image = new System.Windows.Controls.Image { Stretch = Stretch.Uniform, Margin = new Thickness(90, ConsoleTopInset + 40, 90, 110) };
            box.Children.Add(image);

            var info = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Bottom, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
            var title = ConsoleTitle(string.Empty, 22);
            title.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            info.Children.Add(title);
            var meta = HoloLabel(string.Empty);
            meta.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
            info.Children.Add(meta);
            var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            info.Children.Add(buttons);
            box.Children.Add(info);

            int current = index;
            void Show(int i)
            {
                current = (i + list.Count) % list.Count;
                var shot = list[current];
                image.Source = null;
                _ = LoadHoloImageAsync(shot.FilePath, image, 2560);
                title.Text = shot.Label;
                meta.Text = shot.Time.ToString("g", Loc.Culture) + "   ·   " + (current + 1).ToString(Loc.Culture) + " / " + list.Count.ToString(Loc.Culture);
                buttons.Children.Clear();
                buttons.Children.Add(HoloButton("Öffnen", () => OpenShell(shot.FilePath), true, ""));
                buttons.Children.Add(HoloButton("Im Ordner zeigen", () => OpenShell("explorer.exe", $"/select,\"{shot.FilePath}\""), false, ""));
                buttons.Children.Add(HoloButton("Schließen", CloseHoloLightbox, false, ""));
            }

            FrameworkElement Arrow(string glyph, System.Windows.HorizontalAlignment side, int step)
            {
                var arrow = new Grid { Width = 64, Height = 64, HorizontalAlignment = side, VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(18), Cursor = System.Windows.Input.Cursors.Hand, Background = System.Windows.Media.Brushes.Transparent };
                var hex = new System.Windows.Shapes.Path { Data = HexGeometry(60), Margin = new Thickness(2), Fill = MakeBrush("#CC0B1222"), Stroke = MakeBrush("#5522D3EE"), StrokeThickness = 1.5 };
                arrow.Children.Add(hex);
                var icon = new TextBlock { Text = glyph, FontSize = 20, Foreground = System.Windows.Media.Brushes.White, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Center };
                icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
                arrow.Children.Add(icon);
                arrow.MouseEnter += (s, e) => hex.Stroke = new SolidColorBrush(HoloCyan);
                arrow.MouseLeave += (s, e) => hex.Stroke = MakeBrush("#5522D3EE");
                arrow.MouseLeftButtonUp += (s, e) => { e.Handled = true; Show(current + step); };
                return arrow;
            }
            if (list.Count > 1)
            {
                box.Children.Add(Arrow("", System.Windows.HorizontalAlignment.Left, -1));
                box.Children.Add(Arrow("", System.Windows.HorizontalAlignment.Right, 1));
            }

            box.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Escape) { CloseHoloLightbox(); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Left) { Show(current - 1); e.Handled = true; }
                else if (e.Key == System.Windows.Input.Key.Right) { Show(current + 1); e.Handled = true; }
            };
            image.MouseLeftButtonUp += (s, e) => e.Handled = true;
            box.MouseLeftButtonUp += (s, e) => { if (e.OriginalSource == box) CloseHoloLightbox(); };
            box.MouseWheel += (s, e) => { Show(current + (e.Delta < 0 ? 1 : -1)); e.Handled = true; };

            holoLightbox = box;
            holoHost.Children.Add(box);
            Show(index);
            box.Opacity = 0;
            box.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(settings.PageAnimations ? 200 : 1)));
            box.Focus();
        }

        private void CloseHoloLightbox()
        {
            if (holoLightbox == null || holoHost == null) return;
            holoHost.Children.Remove(holoLightbox);
            holoLightbox = null;
        }
    }
}
