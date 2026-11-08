using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Meldungen aufs Handy (Banner auf der Fernbedienungs-Seite und ntfy) ─────────────────────────────

        private sealed class PhoneEvent
        {
            public int Id;
            public string Icon = string.Empty, Title = string.Empty, Text = string.Empty;
            public DateTime At;
        }

        private readonly object phoneEventsLock = new();
        private readonly List<PhoneEvent> phoneEvents = new();
        private int phoneEventId;
        // Wechselt bei jedem Start des Launchers, damit die Handy-Seite alte Meldungsnummern verwirft
        private readonly string phoneEventsBoot = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();

        private const string NtfyServer = "https://ntfy.sh/";

        private bool PhoneNotifyWanted(string kind) => kind switch
        {
            "download" => settings.PhoneNotifyDownload,
            "friend" => settings.PhoneNotifyFriend,
            "battery" => settings.PhoneNotifyBattery,
            _ => true
        };

        /// <summary>Kommt eine Meldung überhaupt irgendwo an (Handy-Seite läuft oder ntfy ist an)?</summary>
        private bool PhoneNotifyActive => remoteServer is { Running: true } || NtfyReady;

        private bool NtfyReady => settings.NtfyEnabled && settings.OnlineFeatures && NtfyTopic().Length > 0;

        /// <summary>Schickt eine Meldung ans Handy. Titel und Text sind schon übersetzt.</summary>
        private void NotifyPhone(string kind, string icon, string title, string text)
        {
            if (!PhoneNotifyWanted(kind)) return;

            if (remoteServer is { Running: true })
            {
                lock (phoneEventsLock)
                {
                    phoneEvents.Add(new PhoneEvent { Id = ++phoneEventId, Icon = icon, Title = title, Text = text, At = DateTime.UtcNow });
                    if (phoneEvents.Count > 20) phoneEvents.RemoveAt(0);
                }
            }

            if (NtfyReady) _ = SendNtfyAsync(icon + " " + title, text);
        }

        /// <summary>Für /api/state: Meldungen nach der Nummer <paramref name="since"/> (höchstens 10 Minuten alt).</summary>
        private object PhoneEventsSince(int since)
        {
            lock (phoneEventsLock)
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-10);
                return new
                {
                    boot = phoneEventsBoot,
                    last = phoneEventId,
                    items = phoneEvents.Where(e => e.Id > since && e.At >= cutoff)
                        .Select(e => new { id = e.Id, icon = e.Icon, title = e.Title, text = e.Text }).ToList()
                };
            }
        }

        // ───────── ntfy ─────────

        private string NtfyTopic() => SecretStore.Unprotect(settings.NtfyTopicProtected);

        /// <summary>Geheimer Kanalname: zufällig, 24 Zeichen aus Kleinbuchstaben und Ziffern, damit ihn niemand errät.</summary>
        private string EnsureNtfyTopic()
        {
            string topic = NtfyTopic();
            if (topic.Length >= 16) return topic;

            const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
            var chars = RandomNumberGenerator.GetBytes(24).Select(b => alphabet[b % alphabet.Length]);
            topic = "dfp-" + new string(chars.ToArray());
            settings.NtfyTopicProtected = SecretStore.Protect(topic);
            SaveSettings();
            return topic;
        }

        private async Task<bool> SendNtfyAsync(string title, string message)
        {
            string topic = NtfyTopic();
            if (topic.Length == 0) return false;

            try
            {
                string json = JsonSerializer.Serialize(new { topic, title, message });
                using var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(NtfyServer, content).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError("ntfy", ex);
                return false;
            }
        }

        // ───────── Einstellungen ─────────

        private void ReadPhoneNotifySettings()
        {
            settings.PhoneNotifyDownload = ChkPhoneDownload.IsChecked == true;
            settings.PhoneNotifyFriend = ChkPhoneFriend.IsChecked == true;
            settings.PhoneNotifyBattery = ChkPhoneBattery.IsChecked == true;

            bool ntfyBefore = settings.NtfyEnabled;
            settings.NtfyEnabled = ChkNtfy.IsChecked == true;
            if (settings.NtfyEnabled && !ntfyBefore) EnsureNtfyTopic();
            UpdateNtfyStatus();
        }

        private void PopulatePhoneNotifySettings()
        {
            ChkPhoneDownload.IsChecked = settings.PhoneNotifyDownload;
            ChkPhoneFriend.IsChecked = settings.PhoneNotifyFriend;
            ChkPhoneBattery.IsChecked = settings.PhoneNotifyBattery;
            ChkNtfy.IsChecked = settings.NtfyEnabled;
            UpdateNtfyStatus();
        }

        private void UpdateNtfyStatus()
        {
            if (TxtNtfyStatus == null) return;

            BtnNtfyPair.IsEnabled = settings.NtfyEnabled;
            BtnPhoneTest.IsEnabled = PhoneNotifyActive;

            if (!settings.NtfyEnabled)
            {
                TxtNtfyStatus.Text = Loc.T("Aus.");
                TxtNtfyStatus.Foreground = BrushSubtle;
            }
            else if (!settings.OnlineFeatures)
            {
                TxtNtfyStatus.Text = Loc.T("Die Online-Funktionen sind ausgeschaltet (Einstellungen, Online-Funktionen und Datenschutz).");
                TxtNtfyStatus.Foreground = MakeBrush("#F59E0B");
            }
            else
            {
                TxtNtfyStatus.Text = Loc.T("An. Koppel dein Handy einmal über „Mit ntfy koppeln“.");
                TxtNtfyStatus.Foreground = MakeBrush("#34D399");
            }
        }

        private void BtnNtfyPair_Click(object sender, RoutedEventArgs e)
        {
            if (StreamerOn)
            {
                Msg("Im Streamer-Modus wird der QR-Code nicht angezeigt, weil er den geheimen Kanal enthält.", "ntfy");
                return;
            }

            string topic = EnsureNtfyTopic();
            string url = NtfyServer + topic;

            var dialog = CreateDialog(Loc.T("Mit ntfy koppeln"), 480, out var panel);
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("1. Installiere die kostenlose App „ntfy“ aus dem Play Store oder App Store.\n2. Tippe in der App auf „+“ und trage den Kanal unten ein (Server: ntfy.sh). Oder scanne den Code und öffne den Link mit der App.\n3. Tippe hier auf „Testmeldung senden“."),
                Foreground = BrushSubtle,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 16)
            });
            panel.Children.Add(new Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Child = new System.Windows.Controls.Image { Source = RenderQr(url), Width = 260, Height = 260, SnapsToDevicePixels = true }
            });

            var topicRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 16) };
            topicRow.Children.Add(new TextBlock { Text = Loc.T("Kanal:") + " ", Foreground = BrushSubtle, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            topicRow.Children.Add(new System.Windows.Controls.TextBox { Text = topic, IsReadOnly = true, FontFamily = new System.Windows.Media.FontFamily("Consolas"), MinWidth = 250, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            var copy = new System.Windows.Controls.Button { Content = Loc.T("Kopieren"), Margin = new Thickness(8, 0, 0, 0) };
            copy.Click += (s2, e2) =>
            {
                try { System.Windows.Clipboard.SetText(topic); } catch { }
                copy.Content = "✓";
            };
            topicRow.Children.Add(copy);
            panel.Children.Add(topicRow);

            var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            var test = new System.Windows.Controls.Button { Content = Loc.T("Testmeldung senden"), Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(18, 8, 18, 8) };
            test.Click += (s2, e2) => BtnPhoneTest_Click(test, e2);
            var close = new System.Windows.Controls.Button { Content = Loc.T("Schließen"), IsCancel = true, Padding = new Thickness(26, 8, 26, 8) };
            close.Click += (s2, e2) => dialog.Close();
            buttons.Children.Add(test);
            buttons.Children.Add(close);
            panel.Children.Add(buttons);
            dialog.ShowDialog();
        }

        private async void BtnPhoneTest_Click(object sender, RoutedEventArgs e)
        {
            string title = Loc.T("Testmeldung");
            string text = Loc.T("So kommen Meldungen vom DFP Pro Launcher auf deinem Handy an.");

            if (remoteServer is { Running: true })
            {
                lock (phoneEventsLock)
                {
                    phoneEvents.Add(new PhoneEvent { Id = ++phoneEventId, Icon = "🧪", Title = title, Text = text, At = DateTime.UtcNow });
                    if (phoneEvents.Count > 20) phoneEvents.RemoveAt(0);
                }
            }

            if (!NtfyReady)
            {
                ShowToast("📱", Loc.T("Meldungen aufs Handy"), remoteServer is { Running: true }
                    ? Loc.T("Gesendet. Sie erscheint auf der geöffneten Fernbedienungs-Seite.")
                    : Loc.T("Schalte die Handy-Fernbedienung oder ntfy ein."), 5, null, true);
                return;
            }

            bool ok = await SendNtfyAsync("🧪 " + title, text);
            ShowToast("📱", Loc.T("Meldungen aufs Handy"), ok
                ? Loc.T("Gesendet. Kommt nichts an, prüfe den Kanal in der ntfy-App.")
                : Loc.T("ntfy ist gerade nicht erreichbar."), 6, null, true);
        }

        // ───────── Steam-Freund startet ein Spiel, das du auch hast ─────────

        private Dictionary<string, string>? friendGames;                 // SteamID → laufendes Spiel (AppID)
        private readonly Dictionary<string, DateTime> friendNotified = new();

        /// <summary>Vergleicht mit dem letzten Stand. Beim ersten Laden wird nur gemerkt, nicht gemeldet.</summary>
        private void CheckFriendGameStarts(List<SteamFriend> friends)
        {
            var now = new Dictionary<string, string>();
            foreach (var friend in friends.Where(f => f.Id.Length > 0)) now[friend.Id] = friend.GameId;

            if (friendGames != null && settings.PhoneNotifyFriend)
            {
                foreach (var friend in friends.Where(f => f.Id.Length > 0 && f.GameId.Length > 0))
                {
                    if (friendGames.TryGetValue(friend.Id, out string? before) && before == friend.GameId) continue;

                    var own = VisibleGames.FirstOrDefault(g => g.Source == "Steam" && g.AppId == friend.GameId);
                    if (own == null) continue;

                    // Pro Freund und Spiel höchstens alle 30 Minuten (Spiel neu gestartet, kurz offline)
                    string key = friend.Id + "|" + friend.GameId;
                    if (friendNotified.TryGetValue(key, out var last) && (DateTime.Now - last).TotalMinutes < 30) continue;
                    friendNotified[key] = DateTime.Now;

                    NotifyPhone("friend", "👥", Loc.T("Steam-Freund spielt"), Loc.T($"{friend.Name} spielt gerade {own.Name}. Du hast das Spiel auch."));
                }
            }

            friendGames = now;
        }
    }
}
