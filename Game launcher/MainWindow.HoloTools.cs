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
        // ───────────────────────────── Thema 2: Holo-Tools („Werkbank“) ─────────────────────────────
        //
        // Links eine Schiene mit allen Gruppen (Werkzeuge und Systembefehle), rechts Tasten-Kacheln mit farbigem
        // Lichtstreifen. Systembefehle zeigen „ADMIN“ und eine Vorschau des Befehls; Rechtsklick kopiert ihn.

        private string holoToolGroup = "all";
        private string holoToolSearch = string.Empty;
        private WrapPanel? holoToolGrid;

        private FrameworkElement BuildHoloTools()
        {
            var tools = ToolDefinitions();
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Tools"), Loc.T("Tools"), Loc.T("Nützliche Windows-Werkzeuge für Gamer, immer griffbereit.")));

            var groups = tools.Select(t => t.Category).Distinct().ToList();
            var commandGroups = SystemCommandGroups.Select(g => g.Group).ToList();
            if (holoToolGroup != "all" && !groups.Contains(holoToolGroup) && !commandGroups.Contains(holoToolGroup)) holoToolGroup = "all";

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(layout);

            var rail = new StackPanel();
            rail.Children.Add(HoloRailItem(Loc.T("Alle"), (tools.Length + SystemCommandGroups.Sum(g => g.Items.Length)).ToString(Loc.Culture), holoToolGroup == "all", () => { holoToolGroup = "all"; RefreshHoloPage(); }, ""));
            foreach (string group in groups)
                rail.Children.Add(HoloRailItem(Loc.T(group), tools.Count(t => t.Category == group).ToString(Loc.Culture), holoToolGroup == group, () => { holoToolGroup = group; RefreshHoloPage(); }, ""));
            var divider = HoloLabel(Loc.T("Systembefehle"), 10.5);
            divider.Margin = new Thickness(16, 14, 0, 8);
            rail.Children.Add(divider);
            foreach (var (group, items) in SystemCommandGroups)
                rail.Children.Add(HoloRailItem(Loc.T(group), items.Length.ToString(Loc.Culture), holoToolGroup == group, () => { holoToolGroup = group; RefreshHoloPage(); }, ""));
            layout.Children.Add(HoloPanel(Loc.T("Gruppen"), rail));

            var body = new StackPanel();
            body.Children.Add(HoloSearchBox("Werkzeug suchen ...", holoToolSearch, text => { holoToolSearch = text; RenderHoloToolGrid(); }));
            body.Children.Add(HoloHint("Ein Klick öffnet ein PowerShell-Fenster mit dem Befehl. Mit 🛡 gekennzeichnete Befehle brauchen Administratorrechte, Windows fragt dann nach. Rechtsklick kopiert den Befehl."));
            holoToolGrid = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            HoloFitWrap(holoToolGrid, 230);
            body.Children.Add(holoToolGrid);
            RenderHoloToolGrid();

            var content = HoloPanel(holoToolGroup == "all" ? Loc.T("Alle Werkzeuge") : Loc.T(holoToolGroup), body);
            Grid.SetColumn(content, 1);
            layout.Children.Add(content);
            return page;
        }

        private void RenderHoloToolGrid()
        {
            if (holoToolGrid == null) return;
            holoToolGrid.Children.Clear();
            bool Match(string title, string text) => holoToolSearch.Length == 0
                || title.Contains(holoToolSearch, StringComparison.CurrentCultureIgnoreCase) || Loc.T(title).Contains(holoToolSearch, StringComparison.CurrentCultureIgnoreCase)
                || Loc.T(text).Contains(holoToolSearch, StringComparison.CurrentCultureIgnoreCase);

            foreach (var tool in ToolDefinitions().Where(t => (holoToolGroup == "all" || t.Category == holoToolGroup) && Match(t.Title, t.Text)))
            {
                string target = tool.Target;
                holoToolGrid.Children.Add(HoloKeycap(tool.Icon, tool.Color, tool.Title, tool.Text, null, false, () => HandleToolTarget(target), null));
            }
            foreach (var (group, items) in SystemCommandGroups.Where(g => holoToolGroup == "all" || g.Group == holoToolGroup))
            {
                foreach (var command in items.Where(c => Match(c.Title, c.Text)))
                {
                    var item = command;
                    var menu = CreateMenu();
                    AddMenuItem(menu, "▶  Ausführen", () => RunSystemCommand(item));
                    AddMenuItem(menu, "📋  Befehl kopieren", () =>
                    {
                        try
                        {
                            System.Windows.Clipboard.SetText(item.Script);
                            ShowToast("📋", "Kopiert", "Der Befehl liegt in der Zwischenablage.", 3);
                        }
                        catch { }
                    });
                    holoToolGrid.Children.Add(HoloKeycap(item.Icon, item.Color, item.Title, item.Text, item.Script, item.Admin, () => RunSystemCommand(item), menu));
                }
            }
            if (holoToolGrid.Children.Count == 0) holoToolGrid.Children.Add(HoloHint("Keine Treffer."));
        }

        /// <summary>Tasten-Kachel: Lichtstreifen oben in der Farbe der Gruppe, großes Symbol, Titel, Text, optional Befehls-Vorschau.</summary>
        private FrameworkElement HoloKeycap(string icon, string color, string title, string text, string? script, bool admin, Action click, System.Windows.Controls.ContextMenu? menu)
        {
            var tint = MakeColor(color);
            var shape = new System.Windows.Shapes.Path { Fill = MakeBrush("#B30D1424"), Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, tint.R, tint.G, tint.B)), StrokeThickness = 1 };
            var stripe = new Border { Height = 3, VerticalAlignment = System.Windows.VerticalAlignment.Top, Margin = new Thickness(0, 0, 14, 0), Background = new SolidColorBrush(tint) };
            if (!settings.PerformanceMode) stripe.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = tint, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };

            var body = new StackPanel { Margin = new Thickness(16, 16, 16, 14) };
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.Children.Add(new TextBlock { Text = icon, FontSize = 26, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Emoji"), VerticalAlignment = System.Windows.VerticalAlignment.Center });
            if (admin)
            {
                var chip = ConsoleChip(null, "🛡 ADMIN", null, false, "#FACC15");
                ((FrameworkElement)chip).HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
                ((FrameworkElement)chip).Margin = new Thickness(0);
                Grid.SetColumn(chip, 1);
                head.Children.Add(chip);
            }
            body.Children.Add(head);

            var name = new TextBlock { Text = Loc.T(title).ToUpper(Loc.Culture), FontSize = 14, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 12, 0, 0) };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            body.Children.Add(name);
            body.Children.Add(new TextBlock { Text = Loc.T(text), FontSize = 12, Foreground = ConsoleMuted, TextWrapping = TextWrapping.Wrap, MaxHeight = 34, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
            if (script != null)
            {
                var code = new TextBlock { Text = "> " + script, FontSize = 10.5, Foreground = ConsoleCyan, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.8 };
                code.SetResourceReference(TextBlock.FontFamilyProperty, "MonoFont");
                body.Children.Add(code);
            }

            var tile = new Grid { Height = script != null ? 168 : 140, Margin = new Thickness(0, 0, 12, 12), Cursor = System.Windows.Input.Cursors.Hand, ContextMenu = menu, ToolTip = Loc.T(text) };
            tile.Children.Add(HoloShapeLayer(shape));
            tile.Children.Add(stripe);
            tile.Children.Add(body);
            tile.SizeChanged += (s, e) => shape.Data = ChamferGeometry(e.NewSize.Width, e.NewSize.Height, 14);

            var lift = new TranslateTransform();
            tile.RenderTransform = lift;
            tile.MouseEnter += (s, e) =>
            {
                shape.Stroke = new SolidColorBrush(tint);
                shape.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x30, tint.R, tint.G, tint.B));
                if (settings.HoverAnimations) lift.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(-4, TimeSpan.FromMilliseconds(140)));
            };
            tile.MouseLeave += (s, e) =>
            {
                shape.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x55, tint.R, tint.G, tint.B));
                shape.Fill = MakeBrush("#B30D1424");
                lift.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(160)));
            };
            tile.MouseLeftButtonUp += (s, e) => click();
            return tile;
        }
    }
}
