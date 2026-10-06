using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Interop = System.Runtime.InteropServices;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Effekte, die auch die Entwickler-Einstellungen auslösen ─────────────────────────────

        private void PlayHalloween()
        {
            EmojiRain(new[] { "🎃", "👻", "🦇", "🕸" }, 34, 7000);
            EggToast("🎃", "Happy Halloween", Loc.T($"Gruselige Grüße, {DisplayUserName()}!"), 8, false);
        }

        private void PlayChristmas()
        {
            EmojiRain(new[] { "❄", "❅", "❄", "🎄", "⭐" }, 70, 9000);
            EggToast("🎄", "Frohe Weihnachten", Loc.T($"Schöne Feiertage, {DisplayUserName()}!"), 8, false);
        }

        private void PlayNewYear()
        {
            Fireworks(7);
            EggToast("🎆", "Frohes neues Jahr", Loc.T($"Auf ein tolles Spielejahr, {DisplayUserName()}!"), 8, false);
        }

        private void PlayBirthday(int years)
        {
            string text = years == 1
                ? Loc.T("Heute vor einem Jahr hast du mich installiert. Alles Gute zum Launcher-Geburtstag!")
                : Loc.T($"Heute vor {years} Jahren hast du mich installiert. Alles Gute zum Launcher-Geburtstag!");
            EggToast("🎂", "Geburtstag", text, 10, false);
            Confetti();
            UnlockSecret("secret_birthday");
        }

        private void PlayNightOwl()
        {
            EggToast("🦉", "Nachteule", "Dein Pudel schläft schon. Du auch bald?", 9, false);
            UnlockSecret("secret_owl");
        }

        private void PlayMilestone(int hours)
        {
            var milestone = PlaytimeMilestones.FirstOrDefault(m => m.Hours == hours);
            if (milestone.Title == null) return;

            ShowToast(milestone.Icon, Loc.T("Meilenstein") + ": " + Loc.T(milestone.Title),
                Loc.T($"Du hast insgesamt {milestone.Hours} Stunden gespielt."), 10, null, true);
            Confetti();
        }

#if DEBUG
        // ───────────────────────────── Entwickler-Einstellungen (nur in Testversionen) ─────────────────────────────

        private void BuildDevPanel()
        {
            DevPanel.Children.Clear();
            WrapPanel row = new();

            void Group(string title)
            {
                DevPanel.Children.Add(new TextBlock
                {
                    Text = title,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 16, 0, 8)
                });
                row = new WrapPanel();
                DevPanel.Children.Add(row);
            }

            void Dev(string text, Action action)
            {
                var button = new System.Windows.Controls.Button { Content = text, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(14, 8, 14, 8) };
                button.Click += (s, e) =>
                {
                    try { action(); }
                    catch (Exception ex)
                    {
                        LogError("Entwickler", ex);
                        Msg("Das hat nicht geklappt:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
                row.Children.Add(button);
            }

            DevPanel.Children.Add(new TextBlock
            {
                Text = "Testversion: " + VersionLabel() + " · Diese Auswahl gibt es nur in Builds aus Visual Studio, nicht im Installer.",
                Foreground = BrushSubtle,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            Group("Feiertage und besondere Tage");
            Dev("🎃 Halloween", PlayHalloween);
            Dev("🎄 Weihnachten", PlayChristmas);
            Dev("🎆 Neujahr", PlayNewYear);
            Dev("🎂 Geburtstag (1 Jahr)", () => PlayBirthday(1));
            Dev("🎂 Geburtstag (3 Jahre)", () => PlayBirthday(3));
            Dev("🤡 1. April", ShowAprilFools);

            Group("Easter Eggs");
            Dev("🕹 Konami-Code", TriggerKonami);
            Dev("🪩 Disco", StartDisco);
            Dev("🐶 Pudel (Wuff)", PudelGreeting);
            Dev("📊 Geheimstatistik", ShowSecretStats);
            Dev("🦉 Nachteule", PlayNightOwl);
            Dev("🍇 TraubeMinze-Animation", PlayTraubeMinze);
            Dev("☁ StardiSkyTTV-Animation", PlayStardiSky);
            Dev("👑 Creator-Ei", PlayCreatorEgg);
            Dev("🏆 Meilenstein 100 h", () => PlayMilestone(100));
            Dev("🏆 Meilenstein 1000 h", () => PlayMilestone(1000));
            Dev("🍀 Seltene Begrüßung", () =>
            {
                EggToast("🍀", "Begrüßung", Loc.T(RareGreetings[random.Next(RareGreetings.Length)]), 6);
                UnlockSecret("secret_lucky");
            });
            Dev("✨ Willkommensanimation", StartWelcomeAnimation);
            Dev("🎊 Konfetti", Confetti);

            Group("Freischaltungen");
            Dev("🌈 RGB-Popup zeigen", ShowRgbUnlockedDialog);
            Dev("🌈 RGB freischalten", () =>
            {
                settings.RgbUnlocked = true;
                SaveSettings();
                UpdateRgbUi();
            });
            Dev("🌈 RGB sperren", () =>
            {
                settings.RgbUnlocked = false;
                settings.RgbAccent = false;
                SaveSettings();
                UpdateRgbUi();
                UpdateRgbAccent();
            });
            Dev("🏆 Alle Geheimerfolge freischalten", () =>
            {
                foreach (var def in SecretAchievementDefs())
                    settings.Achievements[def.Id] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                SaveSettings();
                RefreshStatsExtras();
            });
            Dev("↺ Geheimerfolge zurücksetzen", () =>
            {
                foreach (string key in settings.Achievements.Keys.Where(k => k.StartsWith("secret_", StringComparison.Ordinal)).ToList())
                    settings.Achievements.Remove(key);
                SaveSettings();
                RefreshStatsExtras();
            });
            Dev("↺ Easter-Egg-Gedächtnis löschen", () =>
            {
                settings.EggSeen.Clear();
                settings.MilestonesReached.Clear();
                settings.MilestonesInit = false;
                SaveSettings();
                ShowToast("🧹", "Entwickler", "Die gemerkten Easter Eggs und Meilensteine wurden gelöscht.", 5);
            });

            Group("Controller und Ansicht");
            Dev("🎮 Controller-Modus an/aus", ToggleControllerMode);
            Dev("🎮 Tastenbelegung zeigen", ShowPadMappingDialog);
            Dev("🎮 Controller-Erkennung simulieren", () => ShowToast("🎮", "Controller erkannt", "Xbox-Controller (Test)\n" + Loc.T("Klicke hier für die empfohlene Tastenbelegung."), 12, ShowPadMappingDialog, true));
            Dev("🎠 Cover-Flow an/aus", () =>
            {
                settings.LibraryView = settings.LibraryView == "flow" ? "cards" : "flow";
                SaveSettings();
                ChipViewCards.IsChecked = settings.LibraryView != "flow";
                ChipViewFlow.IsChecked = settings.LibraryView == "flow";
                ApplyLibraryView();
                ApplyFilter();
            });
            Dev("📡 Streamer-Seite öffnen", () => NavigateTo("streamer"));
            Dev("⬇ Download-Anzeige testen", TestDownloadPill);
            Dev("🎮 Controller-Latenz anzeigen", ShowPadLatency);
            Dev("🖤 Zensur-Stil wechseln", () =>
            {
                settings.StreamCensorStyle = settings.StreamCensorStyle == "blur" ? "bar" : "blur";
                SaveSettings();
                ClearAllCensors();
                UpdateStreamerToggles();
                if (StreamerOn) StartCensorTimer();
                ShowToast("🖤", "Zensur-Stil", Loc.T(settings.StreamCensorStyle == "blur" ? "Verschwommen" : "Schwarzer Balken"), 3);
            });
            Dev("⬆ Schnellstart-Auswahl testen", () => ShowAppPicker("Test"));
            Dev("🚀 Auf GitHub hochladen", RunUploadScript);

            Group("OBS (Simulation ohne OBS)");
            Dev("🎥 OBS-Simulation starten", () =>
            {
                StartObsSimulation();
                NavigateTo("streamer");
            });
            Dev("🔴 Simulierter Stream an/aus", () =>
            {
                if (!obsSimulated) StartObsSimulation();
                SimulateObsCommand(obsStreaming ? "StopStream" : "StartStream", null);
            });
            Dev("⏸ Simulierte Aufnahme pausieren", () =>
            {
                if (!obsSimulated) StartObsSimulation();
                if (!obsRecording) SimulateObsCommand("StartRecord", null);
                HandleObsPauseSimulation();
            });
            Dev("🎬 Nächste simulierte Szene", () =>
            {
                if (!obsSimulated) StartObsSimulation();
                int index = obsScenes.IndexOf(obsCurrentScene);
                SimulateObsCommand("SetCurrentProgramScene", new { sceneName = obsScenes[(index + 1) % obsScenes.Count] });
            });
            Dev("🔌 Verbindungsabbruch simulieren", () =>
            {
                if (!obsSimulated) StartObsSimulation();
                obsSimulated = false;
                SetObsState(ObsState.Lost);
            });
            Dev("⏹ OBS-Simulation beenden", StopObsSimulation);
            Dev("🔑 Anmeldung prüfen (Beispiel aus dem Protokoll)", () =>
            {
                // Beispielwerte aus der offiziellen OBS-WebSocket-Dokumentation
                string auth = ObsClient.BuildAuth("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=");
                bool ok = auth == "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=";
                Msg(ok ? "Die Passwort-Anmeldung rechnet richtig." : "Die Passwort-Anmeldung rechnet falsch: " + auth, "OBS");
            });

            Group("Profile und Sicherungen");
            Dev("💾 Sicherung jetzt anlegen", () => BtnBackupSettingsNow_Click(this, new RoutedEventArgs()));
            Dev("🗓 Start-Sicherung prüfen (7 Tage)", () =>
            {
                BackupSettingsIfDue();
                UpdateLastBackupText();
                ShowToast("💾", "Entwickler", Loc.T(TxtLastBackup.Text), 5);
            });
            Dev("📥 Import-Vorschau (ohne Übernehmen)", () =>
            {
                string json;
                lock (settings.SteamIds) json = JsonSerializer.Serialize(settings);
                var copy = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                copy.AccentColor = "#22C55E";
                bool ok = ShowImportPreview(new SettingsImport { Settings = copy, AppVersion = "99.0.0", Exported = DateTime.Now });
                ShowToast("📥", "Entwickler", ok ? "Bestätigt (Test, nichts übernommen)" : "Abgebrochen", 4);
            });
            Dev("🎮 Spielstart simulieren (Profil-Automatik)", () =>
            {
                if (settings.ProfileOnGame.Length == 0) Msg("Wähle zuerst unter Einstellungen → Allgemein → Einstellungs-Profile ein Profil für den Spielstart.", "Entwickler");
                else OnProfileGameStarted();
            });
            Dev("🏁 Spielende simulieren", OnProfileGameEnded);
            Dev("📡 OBS-Start simulieren", () =>
            {
                if (settings.ProfileOnObs.Length == 0) Msg("Wähle zuerst unter Einstellungen → Allgemein → Einstellungs-Profile ein Profil für OBS.", "Entwickler");
                else _ = AutoActivateProfileAsync(settings.ProfileOnObs, false);
            });
            Dev("⏹ OBS-Ende simulieren", () => AutoRestoreProfile(false));

            Group("Streamer-Extras");
            Dev("⏱ Countdown 10 Sekunden", () =>
            {
                NavigateTo("streamer");
                StartGoLive(TimeSpan.FromSeconds(10));
            });
            Dev("🙈 Aufnahme-Schutz an/aus", () => SetCaptureExclude(!settings.CaptureExclude));
            Dev("💬 Dialog mit Benutzername (Zensur-Test)", () =>
                Msg($"Zensur-Test: Windows-Benutzer {Environment.UserName} auf {Environment.MachineName}, Ordner {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}.\n\nIm Streamer-Modus muss diese Zeile verdeckt sein.", "Entwickler"));

            Group("Controller-Extras");
            Dev("⌨ Bildschirmtastatur zeigen", () =>
            {
                if (!controllerMode) ToggleControllerMode();
                OpenPadKeyboard();
            });
            Dev("🔊 Lautstärke-Anzeige", () =>
            {
                if (!controllerMode) ToggleControllerMode();
                OpenPadVolume();
            });
            Dev("⏹ Spiel-beenden-Abfrage", () =>
            {
                if (activeSessions.Count == 0)
                {
                    Msg("Dafür muss ein Spiel laufen. Starte ein Spiel und öffne dann im Controller-Modus das Menü.", "Entwickler");
                    return;
                }
                if (!controllerMode) ToggleControllerMode();
                PadEndGame();
            });
            Dev("⏻ Herunterfahren-Abfrage (nur Anzeige)", () =>
            {
                if (!controllerMode) ToggleControllerMode();
                OpenPadConfirm(Loc.T("PC jetzt herunterfahren?") + " (Test)", Loc.T("⏻  Herunterfahren"), () => ShowToast("⏻", "Entwickler", "Test: Es wurde nichts ausgeführt.", 4));
            });
            Dev("🔁 Test: Starten und Zurück tauschen", () => AssignPadSlot(ResolveLayout(), "confirm", PadMappingFor(ResolveLayout())["back"]));
            Dev("↺ Tastenbelegung zurücksetzen", () => BtnPadRemapReset_Click(this, new RoutedEventArgs()));

            Group("Bibliothek");
            Dev("🎯 Spielvorschlag", ShowGameSuggestion);
            Dev("🎉 Jahresrückblick", () => ShowYearReview(DateTime.Now.Year));
            Dev("🏁 Test-Sitzung (2 Std.) eintragen", () =>
            {
                var game = allGames.FirstOrDefault(g => !g.Hidden);
                if (game == null) return;
                AddYearPlay(game, 7200);
                RecordSession(game, TimeSpan.FromHours(2));
                SaveSettings();
                ShowToast("🏁", "Entwickler", game.Name, 4);
            });
            Dev("⬇ Download fertig (Test)", () => NotifyDownloadDone("Test-Spiel", "Steam"));
            Dev("📏 Spielgrößen jetzt messen", () =>
            {
                StartGameSizeScan(true);
                ShowToast("📏", "Entwickler", "Die Spielgrößen werden im Hintergrund gemessen.", 4);
            });

            Group("Seitenleiste");
            Dev("↔ Position wechseln (links → rechts → unten)", () =>
            {
                settings.SidebarPosition = NavPosition switch { "left" => "right", "right" => "bottom", _ => "left" };
                SaveSettings();
                PopulateNavPosition();
                ApplySidebar(true);
                ShowToast("↔", "Seitenleiste", settings.SidebarPosition, 2);
            });

            Group("Fehlerbericht und Updates");
            Dev("⚠ Fehlerbericht-Vorschau (mit privaten Testdaten)", () =>
            {
                var test = new InvalidOperationException(
                    $"Testfehler: Datei {Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}\\Documents\\spiel.sav von {Environment.UserName} auf {Environment.MachineName}, " +
                    $"Name {settings.UserName}, Mail test@example.com, OBS 192.168.1.23:4455, Link https://discord.com/api/webhooks/123/abc");
                ShowErrorReportDialog(BuildErrorReport("Entwickler-Test", test), false);
            });
            Dev("💥 Unbehandelten Fehler auslösen", () =>
            {
                lastReportPrompt = DateTime.MinValue;
                Dispatcher.BeginInvoke(new Action(() => throw new InvalidOperationException("Absichtlicher Testfehler aus dem Entwickler-Reiter")));
            });
            Dev("🔢 Versionsvergleich prüfen", () =>
            {
                bool Newer(string a, string b) => TryParseAppVersion(a, out var x) && TryParseAppVersion(b, out var y) && CompareAppVersions(x, y) > 0;
                var checks = new (string A, string B, bool Expected)[]
                {
                    ("v1.2.4", "1.2.4-beta", true), ("1.2.4-beta2", "1.2.4-beta", true), ("1.2.4-beta", "1.2.3", true),
                    ("1.2.3", "1.2.4-beta", false), ("1.2.4", "1.2.4", false), ("1.10.0", "1.9.9", true)
                };
                var failed = checks.Where(c => Newer(c.A, c.B) != c.Expected).Select(c => $"{c.A} > {c.B}").ToList();
                Msg(failed.Count == 0 ? $"Alle {checks.Length} Vergleiche stimmen. Installiert: {VersionText()}" : "Falsch: " + string.Join(", ", failed), "Entwickler");
            });

            Group("Controller-Akku (Vorschau)");
            Dev("🔋 Voll", () => PreviewPadBattery(3, false));
            Dev("🔋 Mittel", () => PreviewPadBattery(2, false));
            Dev("🪫 Niedrig", () => PreviewPadBattery(1, false));
            Dev("🪫 Leer", () => PreviewPadBattery(0, false));
            Dev("🔌 Kabel", () => PreviewPadBattery(3, true));

            Group("Dialoge und Meldungen");
            Dev("🔌 Ladekabel-Warnung", () =>
            {
                int percent = 42;
                Msg($"Das Ladekabel ist nicht angeschlossen (Akku: {percent} %). Am Akku laufen Spiele langsamer und der Akku ist schnell leer. Trotzdem starten?",
                    "Ladekabel fehlt", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            });
            Dev("💬 Test-Meldung", () => ShowToast("🧪", "Testmeldung", "So sieht eine Meldung im Launcher aus.", 6));
            Dev("ℹ Über-Dialog", ShowAbout);
            Dev("🔄 Update jetzt prüfen", () => _ = CheckForUpdateAsync(true));
        }
#endif

        // ───────────────────────────── Hochladen auf GitHub (Entwickler) ─────────────────────────────

        private void RunUploadScript()
        {
            string? folder = AppContext.BaseDirectory;
            for (int i = 0; i < 9 && !string.IsNullOrEmpty(folder); i++)
            {
                string script = System.IO.Path.Combine(folder, "Hochladen.cmd");
                if (File.Exists(script))
                {
                    Process.Start(new ProcessStartInfo(script) { UseShellExecute = true, WorkingDirectory = folder });
                    return;
                }
                folder = System.IO.Path.GetDirectoryName(folder);
            }

            Msg("Die Datei „Hochladen.cmd“ wurde nicht gefunden. Lege sie in den Hauptordner deines Projekts (neben die .sln-Datei).", "Hochladen", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
