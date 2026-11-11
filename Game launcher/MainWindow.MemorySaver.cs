using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Game_launcher
{
    // ───────────────────────────── Sparmodus im Hintergrund (RAM freigeben) ─────────────────────────────
    // Ist das Fenster minimiert oder im Infobereich, räumt der Launcher nach kurzer Zeit auf:
    // Speicher-Bereinigung (GC mit Verdichtung) und danach gibt er seinen Arbeitsspeicher an Windows zurück.
    // Die Daten landen dabei in der Standby-Liste von Windows und sind beim Öffnen sofort wieder da.

    public partial class MainWindow
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimum, IntPtr maximum);

        private DispatcherTimer? memorySaverTimer;
        private long lastTrimmedWorkingSet;
        private bool memoryTrimBusy;

        /// <summary>Erste Freigabe kurz nach dem Minimieren, danach in Abständen erneut, falls wieder viel belegt ist.</summary>
        private static readonly TimeSpan MemoryTrimFirstDelay = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan MemoryTrimRepeat = TimeSpan.FromMinutes(2);
        private const long MemoryTrimRegrowBytes = 40L * 1024 * 1024;

        private bool LauncherInBackground => !IsVisible || WindowState == WindowState.Minimized;

        private void InitMemorySaver()
        {
            StateChanged += (s, e) => UpdateMemorySaver();
            IsVisibleChanged += (s, e) => UpdateMemorySaver();
        }

        private void UpdateMemorySaver()
        {
            if (!settings.TrimMemoryInBackground || !LauncherInBackground)
            {
                memorySaverTimer?.Stop();
                lastTrimmedWorkingSet = 0;
                return;
            }

            if (memorySaverTimer == null)
            {
                memorySaverTimer = new DispatcherTimer(DispatcherPriority.Background);
                memorySaverTimer.Tick += (s, e) =>
                {
                    memorySaverTimer.Interval = MemoryTrimRepeat;
                    if (!settings.TrimMemoryInBackground || !LauncherInBackground) { memorySaverTimer.Stop(); return; }

                    // Nur erneut aufräumen, wenn seit dem letzten Mal spürbar Speicher dazugekommen ist
                    long now = CurrentWorkingSet();
                    if (lastTrimmedWorkingSet > 0 && now - lastTrimmedWorkingSet < MemoryTrimRegrowBytes) return;
                    _ = TrimMemoryAsync();
                };
            }

            memorySaverTimer.Stop();
            memorySaverTimer.Interval = MemoryTrimFirstDelay;
            memorySaverTimer.Start();
        }

        private static long CurrentWorkingSet()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                return process.WorkingSet64;
            }
            catch { return 0; }
        }

        /// <summary>Räumt den Speicher auf und gibt den Arbeitsspeicher an Windows zurück. Liefert (vorher, nachher) in Bytes.</summary>
        private async Task<(long Before, long After)> TrimMemoryAsync()
        {
            if (memoryTrimBusy) return (0, 0);
            memoryTrimBusy = true;
            try
            {
                var result = await Task.Run(() =>
                {
                    long before = CurrentWorkingSet();
                    try
                    {
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);

                        using var process = Process.GetCurrentProcess();
                        SetProcessWorkingSetSize(process.Handle, (IntPtr)(-1), (IntPtr)(-1));
                    }
                    catch { }
                    return (before, CurrentWorkingSet());
                });
                lastTrimmedWorkingSet = result.Item2;
                return result;
            }
            finally
            {
                memoryTrimBusy = false;
            }
        }
    }
}
