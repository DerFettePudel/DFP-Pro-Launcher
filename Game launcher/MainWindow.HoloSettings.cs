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
        // ───────────────────────────── Thema 2: Holo-Einstellungen („Konfigurations-Matrix“) ─────────────────────────────
        //
        // Links eine Bereichs-Schiene mit Symbolen und Anzahl, oben eine Suche über alle Bereiche, rechts jede Einstellungs-
        // karte als eigenes Holo-Panel in zwei Spalten. Die Inhalte der Karten (Schalter, Regler, Chips, Felder) werden aus
        // Thema 1 übernommen und umgehängt – Laden, Speichern und alle Handler bleiben so unverändert.

        private sealed class HoloSettingsCard
        {
            public Border Card = null!;
            public StackPanel Body = null!;
            public string Category = string.Empty;
            public string Title = string.Empty;
            public TextBlock? Heading;
        }

        private readonly List<HoloSettingsCard> holoSettingsCards = new();
        private string holoSettingsCategory = string.Empty;
        private string holoSettingsSearch = string.Empty;
        private Grid? holoSettingsColumns;

        private static string SettingsGlyph(string category) => category switch
        {
            "general" => "",
            "look" => "",
            "sidebar" => "",
            "dash" => "",
            "games" => "",
            "perf" => "",
            "control" => "",
            "streamer" => "",
            "auto" => "",
            "data" => "",
            "dev" => "",
            _ => ""
        };

        /// <summary>Karten aus Thema 1 einsammeln (neu dazugekommene auch später) und ihren Inhalt herauslösen.</summary>
        private void CollectHoloSettingsCards()
        {
            if (SettingsCards == null) return;
            foreach (var card in SettingsCards.Children.OfType<Border>())
            {
                if (card.Child is not StackPanel body || holoSettingsCards.Any(c => c.Card == card)) continue;
                var heading = body.Children.Count > 0 ? body.Children[0] as TextBlock : null;
                card.Child = null;
                var entry = new HoloSettingsCard
                {
                    Card = card,
                    Body = body,
                    Category = card.Tag as string ?? "general",
                    Title = heading?.Text ?? string.Empty,
                    Heading = heading
                };
                AdjustSettingsForTheme2(entry);
                holoSettingsCards.Add(entry);
            }
        }

        /// <summary>Zeilen, die in Thema 2 keine Wirkung haben – bleiben hier ausgeblendet (auch bei der Suche).</summary>
        private readonly HashSet<UIElement> holoSettingsHidden = new();

        /// <summary>
        /// Keine Überschneidungen zwischen den Themen: Optionen, die nur Thema 1 betreffen, ausblenden, und Texte, die
        /// von der schwebenden Leiste unten sprechen, auf die Kopfleiste von Thema 2 umschreiben.
        /// </summary>
        private void AdjustSettingsForTheme2(HoloSettingsCard card)
        {
            // Zeile (direktes Kind der Karte), in der eine Überschrift mit diesem Text steht
            (UIElement Row, TextBlock Title)? FindRow(string key)
            {
                string text = Loc.T(key);
                foreach (UIElement child in card.Body.Children)
                {
                    var hit = FindTextBlock(child, t => t.Text == text || t.Text == key);
                    if (hit != null) return (child, hit);
                }
                return null;
            }

            // Nur Thema 1: Farbstil des Pudel-Logos (Thema 2 zeigt immer den Neon-Pudel) und das Einfärben des alten Dashboards
            foreach (string key in new[] { "Logo-Design", "Dashboard passend zum letzten Spiel einfärben" })
                if (FindRow(key) is { } row)
                {
                    row.Row.Visibility = Visibility.Collapsed;
                    holoSettingsHidden.Add(row.Row);
                }

            // Texte an die Kopfleiste von Thema 2 anpassen
            if (FindRow("Position der Leiste") is { } position && FindDescription(position.Title) is TextBlock positionText)
                positionText.Text = Loc.T("Oben als Kopfleiste mit START, BIBLIOTHEK und allen Bereichen, oder links bzw. rechts als Seitenleiste.");
            if (FindRow("Logo und Name in der schwebenden Leiste anzeigen") is { } brand)
            {
                brand.Title.Text = Loc.T("Logo und Name in der Kopfleiste anzeigen");
                if (FindDescription(brand.Title) is TextBlock brandText)
                    brandText.Text = Loc.T("Gilt, wenn die Leiste oben ist. Logo, Name und Untertitel übernehmen deine Einstellungen unter „Logo und Name“.");
            }
        }

        private static TextBlock? FindTextBlock(DependencyObject node, Func<TextBlock, bool> match)
        {
            if (node is TextBlock block) return match(block) ? block : null;
            if (node is System.Windows.Controls.Panel panel)
                foreach (UIElement child in panel.Children)
                    if (FindTextBlock(child, match) is TextBlock found) return found;
            if (node is Decorator { Child: DependencyObject inner }) return FindTextBlock(inner, match);
            return null;
        }

        /// <summary>Die Beschreibung steht direkt unter der Überschrift einer Option.</summary>
        private static TextBlock? FindDescription(TextBlock title)
        {
            if (title.Parent is not System.Windows.Controls.Panel parent) return null;
            int index = parent.Children.IndexOf(title);
            return index >= 0 && index + 1 < parent.Children.Count ? parent.Children[index + 1] as TextBlock : null;
        }

        private FrameworkElement BuildHoloSettings()
        {
            CollectHoloSettingsCards();
            if (holoSettingsCategory.Length == 0) holoSettingsCategory = settingsCategory.Length > 0 ? settingsCategory : "general";

            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Einstellungen"), Loc.T("Einstellungen"), Loc.T("Passe den Launcher an dich an. Unter jeder Option steht, was sie bewirkt.")));

            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            page.Children.Add(layout);

            // ── Schiene: Bereiche wie die Chips von Thema 1 (gleiche Reihenfolge, gleiche Sichtbarkeit) ──
            var rail = new StackPanel();
            var search = HoloSearchBox("Einstellungen durchsuchen ...", holoSettingsSearch, text => { holoSettingsSearch = text; RenderHoloSettingsColumns(); }, 212);
            ((FrameworkElement)search).Margin = new Thickness(0, 0, 0, 14);
            rail.Children.Add(search);
            if (ChipCatGeneral?.Parent is System.Windows.Controls.Panel chips)
            {
                foreach (var chip in chips.Children.OfType<System.Windows.Controls.RadioButton>())
                {
                    if (chip.Visibility != Visibility.Visible || chip.Tag is not string category) continue;
                    int count = holoSettingsCards.Count(c => c.Category == category);
                    if (count == 0) continue;
                    string key = category;
                    rail.Children.Add(HoloRailItem(chip.Content as string ?? category, count.ToString(Loc.Culture), holoSettingsSearch.Length == 0 && holoSettingsCategory == key,
                        () => SelectHoloSettingsCategory(key), SettingsGlyph(key)));
                }
            }
            var railPanel = HoloPanel(Loc.T("Bereiche"), rail);
            railPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            layout.Children.Add(railPanel);

            // ── Karten ──
            holoSettingsColumns = new Grid();
            holoSettingsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            holoSettingsColumns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(holoSettingsColumns, 1);
            layout.Children.Add(holoSettingsColumns);
            RenderHoloSettingsColumns();

            if (holoSettingsCategory == "perf") UpdateMemoryText();
            return page;
        }

        private void SelectHoloSettingsCategory(string category)
        {
            holoSettingsCategory = category;
            holoSettingsSearch = string.Empty;
            settingsCategory = category;
            BuildHoloPage(true);
            holoScroll?.ScrollToVerticalOffset(0);
        }

        /// <summary>Karten der gewählten Bereichs (oder alle Treffer der Suche) auf zwei Spalten verteilen.</summary>
        private void RenderHoloSettingsColumns()
        {
            if (holoSettingsColumns == null) return;
            foreach (var old in holoSettingsColumns.Children.OfType<FrameworkElement>().ToList())
                if (old is System.Windows.Controls.Panel column) foreach (var panel in column.Children.OfType<Grid>().ToList()) ReleaseHoloSettingsBody(panel);
            holoSettingsColumns.Children.Clear();

            var left = new StackPanel();
            var right = new StackPanel { Margin = new Thickness(0) };
            Grid.SetColumn(right, 1);
            holoSettingsColumns.Children.Add(left);
            holoSettingsColumns.Children.Add(right);

            string query = holoSettingsSearch;
            var shown = new List<HoloSettingsCard>();
            foreach (var card in holoSettingsCards)
            {
                bool inCategory = card.Category == holoSettingsCategory;
                if (query.Length == 0)
                {
                    foreach (UIElement child in card.Body.Children) child.Visibility = holoSettingsHidden.Contains(child) ? Visibility.Collapsed : Visibility.Visible;
                    if (inCategory) shown.Add(card);
                    continue;
                }

                // Suche: Titel oder einzelne Zeilen der Karte
                bool titleMatch = card.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                bool any = false;
                for (int i = 0; i < card.Body.Children.Count; i++)
                {
                    var child = card.Body.Children[i];
                    if (child == card.Heading) continue;
                    if (holoSettingsHidden.Contains(child)) { child.Visibility = Visibility.Collapsed; continue; }
                    var text = new System.Text.StringBuilder();
                    CollectTexts(child, text);
                    bool match = titleMatch || text.ToString().Contains(query, StringComparison.CurrentCultureIgnoreCase);
                    child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                    any |= match;
                }
                if (any || titleMatch) shown.Add(card);
            }

            // Gleichmäßig verteilen: jeweils in die Spalte mit weniger Inhalt (grob nach Anzahl der Zeilen)
            double leftWeight = 0, rightWeight = 0;
            foreach (var card in shown)
            {
                if (card.Heading != null) card.Heading.Visibility = Visibility.Collapsed;
                double weight = 2 + card.Body.Children.OfType<UIElement>().Count(c => c.Visibility == Visibility.Visible);
                var title = query.Length > 0 ? card.Title + "  ·  " + Loc.T(CategoryLabel(card.Category)) : card.Title;
                Adopt(card.Body);
                card.Body.Margin = new Thickness(0);
                var panel = HoloPanel(title, card.Body);
                panel.Tag = card;
                if (leftWeight <= rightWeight) { left.Children.Add(panel); leftWeight += weight; }
                else { right.Children.Add(panel); rightWeight += weight; }
            }

            if (shown.Count == 0) left.Children.Add(HoloPanel(Loc.T("Einstellungen"), HoloHint("Keine Treffer.")));
        }

        /// <summary>Inhalt einer Karte aus ihrem Holo-Panel lösen (damit er beim nächsten Aufbau wieder umziehen kann).</summary>
        private static void ReleaseHoloSettingsBody(Grid panel)
        {
            if (panel.Tag is HoloSettingsCard card && card.Body.Parent is System.Windows.Controls.Panel parent) parent.Children.Remove(card.Body);
        }

        private string CategoryLabel(string category)
        {
            if (ChipCatGeneral?.Parent is System.Windows.Controls.Panel chips)
                foreach (var chip in chips.Children.OfType<System.Windows.Controls.RadioButton>())
                    if (chip.Tag as string == category) return chip.Content as string ?? category;
            return category;
        }
    }
}
