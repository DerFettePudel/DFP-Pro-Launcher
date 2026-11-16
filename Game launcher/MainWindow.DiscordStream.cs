using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ═════════════════════════════ Streamer-Modus automatisch beim Discord-Stream ═════════════════════════════
        //
        // Hauptquelle ist das Log von Discord (siehe unten). Zusätzlich zählt die Windows-Bildschirmaufnahme:
        // Windows trägt laufende Aufnahmen unter CapabilityAccessManager ein (LastUsedTimeStop = 0), aber nicht verlässlich.
        // Kurze Vorschaubilder im Auswahlfenster von Discord dauern nur Millisekunden – darum zählt eine Aufnahme
        // erst, wenn sie bei zwei Prüfungen hintereinander (5 s Abstand) läuft.

        private bool discordAutoOn;
        private bool discordAutoSuppressed;
        private int discordShareHits;
        private Border? streamerPill;
        private TextBlock? streamerPillDetail;

        private static readonly string[] DiscordProcessNames = { "Discord", "DiscordPTB", "DiscordCanary" };

        private async Task CheckDiscordStreamAsync()
        {
            bool sharing = settings.DiscordAutoStreamer && await Task.Run(IsDiscordSharingScreen);
            discordShareHits = sharing ? Math.Min(discordShareHits + 1, 2) : 0;

            if (discordShareHits >= 2)
            {
                if (discordAutoOn || discordAutoSuppressed) return;

                if (!StreamerOn)
                    ShowToast("📡", "Streamer-Modus aktiv", "Du streamst in Discord. Private Angaben sind verborgen.", 6);
                discordAutoOn = true;
                ApplyStreamerMode();
                RenderStreamerPageIfVisible();
            }
            else if (discordShareHits == 0)
            {
                discordAutoSuppressed = false;
                if (!discordAutoOn) return;

                discordAutoOn = false;
                ApplyStreamerMode();
                if (!StreamerOn) ShowToast("📡", "Streamer-Modus aus", "Dein Discord-Stream ist beendet. Alle Angaben sind wieder sichtbar.", 5);
                RenderStreamerPageIfVisible();
            }
        }

        // ───────── Discord-Log: Discord schreibt Start und Ende des eigenen Streams in logs\renderer_js.log ─────────
        //   [startStreamWithSource] Starting stream for source …          → eigener Stream beginnt
        //   [RTCConnection(<id>, stream)] Connecting to RTC server …      → Verbindung dieses Streams
        //   [Connection(stream)] capturing desktop …                      → nur beim eigenen Stream (nicht beim Zuschauen)
        //   [RTCConnection(<id>, stream)] Destroy RTCConnection           → Stream beendet

        private sealed class DiscordLogState
        {
            public long Offset = -1;
            public string Rest = string.Empty;
            public string LastStreamConnection = string.Empty;
            public readonly HashSet<string> OwnStreams = new();
        }

        private static readonly Dictionary<string, DiscordLogState> discordLogs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Regex DiscordConnectRegex = new(@"RTCConnection\((\d+), stream\)\] Connecting to RTC server", RegexOptions.Compiled);
        private static readonly Regex DiscordDestroyRegex = new(@"RTCConnection\((\d+), stream\)\] Destroy RTCConnection", RegexOptions.Compiled);

        private static bool IsDiscordStreamingByLog(DateTime discordStarted)
        {
            bool streaming = false;
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            foreach (string folder in new[] { "discord", "discordptb", "discordcanary" })
            {
                string file = System.IO.Path.Combine(roaming, folder, "logs", "renderer_js.log");
                if (!File.Exists(file)) continue;

                try
                {
                    if (!discordLogs.TryGetValue(file, out var state)) discordLogs[file] = state = new DiscordLogState();

                    using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    long length = stream.Length;

                    // Erster Blick oder Datei neu angelegt: die letzten 8 MB reichen für die laufende Sitzung
                    if (state.Offset < 0 || length < state.Offset)
                    {
                        state.Offset = Math.Max(0, length - 8 * 1024 * 1024);
                        state.Rest = string.Empty;
                        state.LastStreamConnection = string.Empty;
                        state.OwnStreams.Clear();
                    }

                    if (length > state.Offset)
                    {
                        stream.Seek(state.Offset, SeekOrigin.Begin);
                        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                        string text = state.Rest + reader.ReadToEnd();
                        state.Offset = length;

                        int lastBreak = text.LastIndexOf('\n');
                        state.Rest = lastBreak < 0 ? text : text.Substring(lastBreak + 1);
                        if (lastBreak >= 0) ParseDiscordLog(text.Substring(0, lastBreak), state, discordStarted);
                    }

                    if (state.OwnStreams.Count > 0) streaming = true;
                }
                catch { }
            }

            return streaming;
        }

        private static void ParseDiscordLog(string text, DiscordLogState state, DateTime discordStarted)
        {
            foreach (string line in text.Split('\n'))
            {
                if (line.IndexOf("stream", StringComparison.OrdinalIgnoreCase) < 0) continue;

                // Zeilen aus einer früheren Discord-Sitzung zählen nicht (Discord beendet = Stream beendet)
                if (line.Length > 25 && line[0] == '[' &&
                    DateTime.TryParseExact(line.Substring(1, 23), "yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeLocal, out var time) &&
                    time < discordStarted.AddMinutes(-1))
                    continue;

                var connect = DiscordConnectRegex.Match(line);
                if (connect.Success)
                {
                    state.LastStreamConnection = connect.Groups[1].Value;
                    continue;
                }

                if (line.Contains("[Connection(stream)] capturing", StringComparison.Ordinal) && state.LastStreamConnection.Length > 0)
                {
                    state.OwnStreams.Add(state.LastStreamConnection);
                    continue;
                }

                var destroy = DiscordDestroyRegex.Match(line);
                if (destroy.Success) state.OwnStreams.Remove(destroy.Groups[1].Value);
            }
        }

        private static bool IsDiscordSharingScreen()
        {
            try
            {
                // Nur Aufnahmen zählen, die nach dem Start des laufenden Discord begonnen haben (alte Reste nach einem Absturz nicht)
                DateTime? discordStarted = null;
                foreach (string name in DiscordProcessNames)
                {
                    foreach (var process in Process.GetProcessesByName(name))
                    {
                        try
                        {
                            var started = process.StartTime;
                            if (discordStarted == null || started < discordStarted) discordStarted = started;
                        }
                        catch { }
                        finally { process.Dispose(); }
                    }
                }
                if (discordStarted == null)
                {
                    lock (discordLogs)
                    {
                        foreach (var state in discordLogs.Values) state.OwnStreams.Clear();
                    }
                    return false;
                }

                lock (discordLogs)
                {
                    if (IsDiscordStreamingByLog(discordStarted.Value)) return true;
                }

                // Zusätzlich: Windows-Bildschirmaufnahme (trägt Discord nicht immer ein)
                long notBefore = discordStarted.Value.AddMinutes(-1).ToFileTime();

                const string store = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
                foreach (string capability in new[] { "graphicsCaptureWithoutBorder", "graphicsCaptureProgrammatic" })
                {
                    using var root = Registry.CurrentUser.OpenSubKey(store + capability + @"\NonPackaged");
                    if (root == null) continue;

                    foreach (string sub in root.GetSubKeyNames())
                    {
                        // Schlüssel = Pfad mit # statt \, z.B. C:#Users#…#Discord#app-1.0.9268#Discord.exe
                        string exe = sub.Substring(sub.LastIndexOf('#') + 1);
                        if (!DiscordProcessNames.Any(n => string.Equals(exe, n + ".exe", StringComparison.OrdinalIgnoreCase))) continue;

                        using var key = root.OpenSubKey(sub);
                        if (key == null) continue;

                        long start = Convert.ToInt64(key.GetValue("LastUsedTimeStart") ?? 0L);
                        long stop = Convert.ToInt64(key.GetValue("LastUsedTimeStop") ?? 0L);
                        if (stop == 0 && start >= notBefore) return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // ───────── Sichtbarer Knopf oben im Fenster, solange der Streamer-Modus an ist ─────────

        private void UpdateStreamerPill(bool on)
        {
            if (!on)
            {
                if (streamerPill != null) streamerPill.Visibility = Visibility.Collapsed;
                return;
            }

            if (streamerPill == null)
            {
                if (ToastHost.Parent is not System.Windows.Controls.Panel host) return;
                streamerPill = BuildStreamerPill();
                host.Children.Add(streamerPill);
            }

            streamerPillDetail!.Text = settings.StreamerMode ? Loc.T("manuell eingeschaltet")
                : discordAutoOn ? Loc.T("Discord-Stream erkannt")
                : obsAutoOn ? Loc.T("OBS sendet")
                : Loc.T("Streaming-Software läuft");
            streamerPill.Visibility = Visibility.Visible;
        }

        private Border BuildStreamerPill()
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = MakeBrush("#EF4444"),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 10, 0)
            };
            if (!settings.PerformanceMode)
            {
                dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(900))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }

            var texts = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            texts.Children.Add(new TextBlock
            {
                Text = Loc.T("Streamer-Modus an"),
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 13
            });
            streamerPillDetail = new TextBlock { Foreground = MakeBrush("#FCA5A5"), FontSize = 11 };
            texts.Children.Add(streamerPillDetail);

            var button = new System.Windows.Controls.Button
            {
                Content = Loc.T("Ausschalten"),
                Padding = new Thickness(12, 4, 12, 4),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = Loc.T("Zeigt alle Angaben wieder an. Bleibt aus, bis der Stream endet.")
            };
            button.Click += (s, e) =>
            {
                ToggleStreamerMode();
                RenderStreamerPageIfVisible();
                if (holoKey == "streamer") RequestConsoleRefresh();
            };

            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            row.Children.Add(dot);
            row.Children.Add(texts);
            row.Children.Add(button);

            var pill = new Border
            {
                Background = MakeBrush("#E6200A0E"),
                BorderBrush = MakeBrush("#99EF4444"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(14, 6, 6, 6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                Child = row
            };
            System.Windows.Controls.Panel.SetZIndex(pill, 155);
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(pill, true);
            return pill;
        }
    }
}
