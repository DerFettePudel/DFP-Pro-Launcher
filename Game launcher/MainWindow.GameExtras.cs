using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Spiel-Extras: Begleitprogramme schließen ─────────────────────────────

        // Prozessnamen der Begleitprogramme, die der Launcher selbst gestartet hat (vorher laufende bleiben offen)
        private readonly HashSet<string> startedCompanions = new(StringComparer.OrdinalIgnoreCase);

        private void ReadGameExtrasSettings()
        {
            settings.CloseCompanionsAfterGame = ChkCloseCompanions.IsChecked == true;
        }

        private void PopulateGameExtrasSettings()
        {
            ChkCloseCompanions.IsChecked = settings.CloseCompanionsAfterGame;
        }

        /// <summary>Kein Spiel läuft mehr: aufräumen, was für die Spiele gestartet oder umgestellt wurde.</summary>
        private void OnAllGamesEnded()
        {
            CloseStartedCompanions();
        }

        private void CloseStartedCompanions()
        {
            var names = startedCompanions.ToList();
            startedCompanions.Clear();
            if (!settings.CloseCompanionsAfterGame || names.Count == 0) return;

            // Nur freundlich schließen (wie das X im Fenster), nie hart beenden: OBS & Co. dürfen nichts verlieren
            Task.Run(() =>
            {
                foreach (string name in names)
                {
                    if (ProtectedProcesses.Any(p => p.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                    try
                    {
                        foreach (var process in Process.GetProcessesByName(name))
                        {
                            try { process.CloseMainWindow(); }
                            catch { }
                            finally { process.Dispose(); }
                        }
                    }
                    catch { }
                }
            });
        }
    }
}
