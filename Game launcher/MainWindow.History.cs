using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Zurück / Vor wie im Browser (Maus-Seitentasten, Alt + ← / →) ─────────────────────────────

        private const int NavHistoryLimit = 30;
        private readonly List<FrameworkElement> navBack = new();
        private readonly List<FrameworkElement> navForward = new();
        private FrameworkElement? navCurrent;
        private bool navByHistory;

        private void InitNavHistory()
        {
            PreviewMouseDown += OnHistoryMouseDown;
            PreviewKeyDown += OnHistoryKeyDown;
        }

        /// <summary>Wird von ShowView bei jedem Seitenwechsel aufgerufen.</summary>
        private void RecordNavigation(FrameworkElement view)
        {
            if (navCurrent == view) return;

            if (!navByHistory && navCurrent != null)
            {
                navBack.Add(navCurrent);
                if (navBack.Count > NavHistoryLimit) navBack.RemoveAt(0);
                navForward.Clear();   // neuer Weg: „Vor“ gibt es dann nicht mehr
            }
            navCurrent = view;
        }

        private string? ViewKey(FrameworkElement view)
        {
            if (view == ViewDashboard) return "dashboard";
            if (view == ViewGames) return "games";
            if (view == ViewStats) return "stats";
            if (view == ViewApps) return "apps";
            if (view == ViewLinks) return "links";
            if (view == ViewWishlist) return "wishlist";
            if (view == ViewDownloads) return "downloads";
            if (view == ViewSystem) return "system";
            if (view == ViewStorage) return "storage";
            if (view == ViewOptimization) return "optimization";
            if (view == ViewTools) return "tools";
            if (view == ViewDeals) return "deals";
            if (view == ViewBacklog) return "backlog";
            if (view == ViewGallery) return "gallery";
            if (view == ViewStreamer) return "streamer";
            if (view == ViewSettings) return "settings";
            return null;
        }

        private bool NavigateHistory(bool back)
        {
            var from = back ? navBack : navForward;
            var to = back ? navForward : navBack;

            while (from.Count > 0)
            {
                var target = from[^1];
                from.RemoveAt(from.Count - 1);
                string? key = ViewKey(target);
                if (key == null || target == navCurrent) continue;

                if (navCurrent != null) to.Add(navCurrent);
                navByHistory = true;
                try { NavigateTo(key); }
                finally { navByHistory = false; }
                return true;
            }
            return false;
        }

        /// <summary>Erst offene Ebenen schließen (Suche, Tastenkürzel), dann eine Seite zurück.</summary>
        private void HistoryBack()
        {
            if (SpotlightLayer.Visibility == Visibility.Visible)
            {
                CloseSpotlight();
                return;
            }
            if (ConsoleBack()) return;   // Thema 2: von den Spiel-Details zurück
            NavigateHistory(true);
        }

        private void OnHistoryMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (controllerMode) return;
            if (e.ChangedButton == MouseButton.XButton1)
            {
                HistoryBack();
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.XButton2)
            {
                NavigateHistory(false);
                e.Handled = true;
            }
        }

        private void OnHistoryKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (controllerMode || e.Key != Key.System || Keyboard.Modifiers != ModifierKeys.Alt) return;
            if (e.SystemKey == Key.Left)
            {
                HistoryBack();
                e.Handled = true;
            }
            else if (e.SystemKey == Key.Right)
            {
                NavigateHistory(false);
                e.Handled = true;
            }
        }
    }
}
