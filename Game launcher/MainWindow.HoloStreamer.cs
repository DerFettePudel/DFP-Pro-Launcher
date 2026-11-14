using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Thema 2: Holo-Streamer („Sendezentrale“) ─────────────────────────────
        //
        // Neuer Aufbau: Kopf mit großem ON-AIR-Schild, Streamer-Modus als Schalter, OBS mit Live-Werten als Anzeigen,
        // Countdown groß. Die Innenteile, die Thema 1 laufend selbst neu füllt (Szenen, Countdown-Optionen, Programme,
        // Schnellzugriffe, Checkliste), werden in die Holo-Panels umgehängt – so bleiben alle Funktionen live.

        /// <summary>Element aus seinem bisherigen Behälter lösen, damit es in ein Holo-Panel umziehen kann.</summary>
        private static T Adopt<T>(T element) where T : FrameworkElement
        {
            switch (element.Parent)
            {
                case System.Windows.Controls.Panel panel: panel.Children.Remove(element); break;
                case Decorator decorator: decorator.Child = null; break;
                case ContentControl control: control.Content = null; break;
            }
            element.Visibility = Visibility.Visible;
            return element;
        }

        private FrameworkElement BuildHoloStreamer()
        {
            bool on = StreamerOn;
            var page = new StackPanel();
            page.Children.Add(HoloHeader(Loc.T("Streamer"), Loc.T("Streamer"), Loc.T("Alles für deinen Stream: Programme, Schnellzugriffe und eine Checkliste vor dem Livegang.")));

            // ── ON AIR + Streamer-Modus ──
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sign = new Grid { Margin = new Thickness(0, 0, 26, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var signPlate = new Border { BorderThickness = new Thickness(2), RenderTransform = new SkewTransform(-12, 0), Background = on ? MakeBrush("#33EF4444") : MakeBrush("#0F141C2E"), BorderBrush = MakeBrush(on ? "#EF4444" : "#3A4766") };
            if (on && !settings.PerformanceMode) signPlate.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = MakeColor("#EF4444"), BlurRadius = 24, ShadowDepth = 0, Opacity = 0.9 };
            sign.Children.Add(signPlate);
            var signText = new TextBlock { Text = on ? "ON AIR" : "OFF AIR", FontSize = 34, FontWeight = FontWeights.Bold, Foreground = MakeBrush(on ? "#FF5A5A" : "#5B6B86"), Margin = new Thickness(26, 8, 26, 8) };
            signText.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            sign.Children.Add(signText);
            top.Children.Add(sign);

            var state = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center };
            state.Children.Add(HoloLabel(Loc.T("Streamer-Modus")));
            state.Children.Add(new TextBlock { Text = TxtStreamerState?.Text ?? string.Empty, FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = on ? MakeBrush("#34D399") : ConsoleMuted, Margin = new Thickness(0, 2, 0, 4) });
            state.Children.Add(HoloHint("Verbirgt Name, Standort, Statistiken, Pfade und den Musik-Player. Was genau verborgen wird, stellst du unter Einstellungen → Streamer ein."));
            Grid.SetColumn(state, 1);
            top.Children.Add(state);

            var toggle = HoloToggle(on, _ => BtnStreamerToggle_Click(this, new RoutedEventArgs()));
            toggle.LayoutTransform = new ScaleTransform(1.5, 1.5);
            toggle.Margin = new Thickness(20, 0, 6, 0);
            Grid.SetColumn(toggle, 2);
            top.Children.Add(toggle);
            page.Children.Add(HoloPanel(Loc.T("Sendestatus"), top, null, 26));

            var left = new StackPanel();
            var right = new StackPanel();
            page.Children.Add(HoloColumns(left, right, 0.45));

            // ── OBS ──
            var obs = new StackPanel();
            var obsHead = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var dot = new System.Windows.Shapes.Path { Data = HexGeometry(14), Width = 14, Height = 14, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            var status = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = System.Windows.VerticalAlignment.Center };
            obsHead.Children.Add(dot);
            obsHead.Children.Add(status);
            obs.Children.Add(obsHead);
            var hint = HoloHint(string.Empty);
            obs.Children.Add(hint);

            var offline = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            offline.Children.Add(HoloButton("Jetzt verbinden", () => BtnObsConnect_Click(this, new RoutedEventArgs()), true, ""));
            offline.Children.Add(HoloButton("OBS-Einstellungen", () => BtnObsSettings_Click(this, new RoutedEventArgs()), false, ""));
            obs.Children.Add(offline);

            var online = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            var controls = new WrapPanel();
            var streamButton = new ContentControl();
            var recordButton = new ContentControl();
            var micButton = new ContentControl();
            controls.Children.Add(streamButton);
            controls.Children.Add(recordButton);
            controls.Children.Add(micButton);
            online.Children.Add(controls);

            var gauges = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Margin = new Thickness(0, 8, 0, 12) };
            TextBlock Gauge(string caption)
            {
                var cell = new StackPanel();
                cell.Children.Add(HoloLabel(Loc.T(caption)));
                var value = ConsoleTitle("–", 22);
                cell.Children.Add(value);
                gauges.Children.Add(cell);
                return value;
            }
            var live = Gauge("Stream");
            var rec = Gauge("Aufnahme");
            var dropped = Gauge("Verlorene Bilder");
            online.Children.Add(gauges);
            online.Children.Add(HoloLabel(Loc.T("Szenen")));
            if (ObsScenePanel != null)
            {
                var scenes = Adopt(ObsScenePanel);
                scenes.Margin = new Thickness(0, 8, 0, 0);
                online.Children.Add(scenes);
            }
            obs.Children.Add(online);
            left.Children.Add(HoloPanel("OBS", obs));

            // ── Countdown ──
            var countdown = new StackPanel();
            var time = ConsoleTitle(string.Empty, 64);
            time.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            countdown.Children.Add(time);
            countdown.Children.Add(HoloHint("Zählt bis zum Livegang herunter. Die Checkliste unten aktualisiert sich dabei laufend, am Ende erscheint eine Meldung."));
            if (GoLiveOptions != null)
            {
                var options = Adopt(GoLiveOptions);
                options.Margin = new Thickness(0, 12, 0, 0);
                countdown.Children.Add(options);
            }
            var countdownButtons = new WrapPanel { Margin = new Thickness(0, 6, 0, -10) };
            var goLive = new ContentControl();
            countdownButtons.Children.Add(goLive);
            countdownButtons.Children.Add(HoloButton("Ablauf „Stream-Start“ anlegen", () => BtnGoLiveRoutine_Click(this, new RoutedEventArgs()), false, ""));
            countdown.Children.Add(countdownButtons);
            left.Children.Add(HoloPanel(Loc.T("Countdown „Gleich live“"), countdown));

            // ── Checkliste ──
            if (StreamerChecklist != null) right.Children.Add(HoloPanel(Loc.T("Checkliste vor dem Livegang"), Adopt(StreamerChecklist)));

            // ── Programme, Schnellzugriffe ──
            var apps = new StackPanel();
            apps.Children.Add(HoloHint(TxtStreamerAppsHint?.Text ?? string.Empty));
            if (StreamerAppsPanel != null) apps.Children.Add(Adopt(StreamerAppsPanel));
            right.Children.Add(HoloPanel(Loc.T("Streaming-Programme"), apps));

            var links = new StackPanel();
            links.Children.Add(HoloHint("Rechtsklick auf einen Link bearbeitet oder entfernt ihn. Mit der Plus-Kachel fügst du eigene hinzu."));
            if (StreamerLinksPanel != null) links.Children.Add(Adopt(StreamerLinksPanel));
            right.Children.Add(HoloPanel(Loc.T("Schnellzugriffe"), links));

            // ── Live: Texte und Knöpfe von Thema 1 spiegeln ──
            string lastStream = "", lastRecord = "", lastMic = "", lastGo = "";
            StartHoloLive(() =>
            {
                if ((DateTime.Now - holoLiveSlow).TotalMilliseconds < 500) return;
                holoLiveSlow = DateTime.Now;

                status.Text = TxtObsStatus?.Text ?? string.Empty;
                var fill = ObsStatusDot?.Fill ?? MakeBrush("#6B7280");
                dot.Fill = fill;
                hint.Text = TxtObsHint?.Text ?? string.Empty;
                bool connected = ObsControls?.Visibility == Visibility.Visible;
                offline.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
                online.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
                live.Text = TxtObsLive?.Text ?? "–";
                rec.Text = TxtObsRecTime?.Text ?? "–";
                dropped.Text = TxtObsDropped?.Text ?? "–";

                string streamText = BtnObsStream?.Content as string ?? Loc.T("Stream starten");
                if (streamText != lastStream) { lastStream = streamText; streamButton.Content = HoloButton(streamText, () => BtnObsStream_Click(this, new RoutedEventArgs()), true, ""); }
                string recordText = BtnObsRecord?.Content as string ?? Loc.T("Aufnahme starten");
                if (recordText != lastRecord) { lastRecord = recordText; recordButton.Content = HoloButton(recordText, () => BtnObsRecord_Click(this, new RoutedEventArgs()), false, ""); }
                string micText = BtnObsMic?.Content as string ?? string.Empty;
                if (micText != lastMic) { lastMic = micText; micButton.Content = HoloButton(micText, () => BtnObsMic_Click(this, new RoutedEventArgs()), false); }

                bool counting = TxtGoLiveTime?.Visibility == Visibility.Visible;
                time.Text = counting ? TxtGoLiveTime!.Text : "—";
                string goText = BtnGoLive?.Content as string ?? Loc.T("Countdown starten");
                if (goText != lastGo) { lastGo = goText; goLive.Content = HoloButton(goText, () => BtnGoLive_Click(this, new RoutedEventArgs()), true, ""); }
            });
            return page;
        }
    }
}
