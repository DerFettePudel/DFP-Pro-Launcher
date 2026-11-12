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
        // ───────────────────────────── Fenster: genau maximieren, Taskleiste im Vollbild ausblenden ─────────────────────────────

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeExtras.NativeRect Monitor;
            public NativeExtras.NativeRect Work;
            public uint Flags;
        }

        [Interop.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);

        private const int WmGetMinMaxInfo = 0x0024;
        private const int WmSysCommand = 0x0112, ScMaximize = 0xF030;
        private bool syncingMaximize;
        private const uint MonitorDefaultToNearest = 2;
        private bool taskbarAutoHideByUs;
        private bool windowTransition;   // kurzer Zwischenschritt (Normal → Maximiert): Taskleiste dabei nicht umschalten
        private bool refitRunning;

        private void InitExtras23()
        {
            SourceInitialized += (s, e) =>
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                System.Windows.Interop.HwndSource.FromHwnd(handle)?.AddHook(MaximizeHook);
            };

            // Rechtsklick-Menüs im Launcher-Design auch dort, wo sie nicht im Fenster entstehen (Textfelder, Dialoge)
            try
            {
                if (System.Windows.Application.Current != null && TryFindResource("DarkContextMenu") is Style menuStyle)
                    System.Windows.Application.Current.Resources[typeof(ContextMenu)] = menuStyle;
            }
            catch { }

            // Schon beim Start maximiert (gemerkte Fenstergröße): gleich ohne Windows-Rahmen maximieren
            if (WindowState == WindowState.Maximized) WindowStyle = WindowStyle.None;

            // Ist der Launcher letztes Mal abgestürzt, während die Taskleiste ausgeblendet war: zuerst zurückstellen
            if (settings.TaskbarAutoHideByLauncher) RestoreTaskbar();

            StateChanged += (s, e) =>
            {
                SyncMaximizeStyle();
                UpdateFullscreenState();
            };
            IsVisibleChanged += (s, e) => UpdateFullscreenState();
            Loaded += (s, e) => UpdateFullscreenState();
            Closed += (s, e) => RestoreTaskbar();
            if (System.Windows.Application.Current is { } app) app.Exit += (s, e) => RestoreTaskbar();
        }

        /// <summary>
        /// Legt die Größe beim Maximieren selbst fest: genau der Arbeitsbereich (ohne Überstand über den Bildschirmrand,
        /// der sonst als dünne Linie oben sichtbar werden kann) oder, im Vollbild, der ganze Bildschirm.
        /// </summary>
        private IntPtr MaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Maximieren per Doppelklick auf die Leiste oder Systemmenü: vorher den Rahmen abschalten (siehe SyncMaximizeStyle)
            if (msg == WmSysCommand && ((int)wParam.ToInt64() & 0xFFF0) == ScMaximize && !controllerMode)
            {
                if (WindowStyle != WindowStyle.None) WindowStyle = WindowStyle.None;
                return IntPtr.Zero;
            }
            // Taskleiste verschoben, vergrößert oder anders eingestellt: schwebende Leiste neu ausrichten
            if (msg == WmSettingChange) Dispatcher.BeginInvoke(new Action(UpdateDockTaskbarLift), System.Windows.Threading.DispatcherPriority.Background);
            if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

                // Vollbild (ganzer Bildschirm): Windows legt die Taskleiste dann von selbst dahinter, solange der Launcher vorne ist
                bool full = controllerMode || settings.TaskbarHideMaximized;
                var area = full ? info.Monitor : info.Work;

                var mmi = Interop.Marshal.PtrToStructure<MinMaxInfo>(lParam);
                mmi.MaxPosition.X = area.Left - info.Monitor.Left;
                mmi.MaxPosition.Y = area.Top - info.Monitor.Top;
                mmi.MaxSize.X = area.Right - area.Left;
                mmi.MaxSize.Y = area.Bottom - area.Top;
                Interop.Marshal.StructureToPtr(mmi, lParam, true);
                // handled bleibt false: WPF ergänzt danach noch die Mindestgröße des Fensters
            }
            catch { }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Windows übergeht die eigene Größe beim Maximieren, solange das Fenster einen Windows-Rahmen hat, und lässt es
        /// dann ein paar Pixel über den Bildschirmrand ragen (daher die dünne Linie oben). Ohne Rahmen (wie im Controller-Modus)
        /// gilt die Größe pixelgenau. Optisch ändert sich nichts, weil der Launcher seine eigene Fensterleiste hat.
        /// </summary>
        private void SyncMaximizeStyle()
        {
            if (controllerMode || syncingMaximize) return;

            syncingMaximize = true;
            try
            {
                if (WindowState == WindowState.Maximized && WindowStyle != WindowStyle.None)
                {
                    // auf anderem Weg maximiert (zum Beispiel Win+Pfeil oder an den oberen Rand gezogen): neu maximieren
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Normal;
                    WindowState = WindowState.Maximized;
                }
                else if (WindowState == WindowState.Normal && WindowStyle == WindowStyle.None)
                {
                    WindowStyle = WindowStyle.SingleBorderWindow;
                }
            }
            finally
            {
                syncingMaximize = false;
            }
            FixMaximizedOverhang();
        }

        // ───────── Taskleiste im Vollbild: Windows-Funktion „automatisch ausblenden“ ─────────

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct AppBarData
        {
            public int Size;
            public IntPtr Window;
            public uint CallbackMessage;
            public uint Edge;
            public NativeExtras.NativeRect Rect;
            public IntPtr Param;
        }

        [Interop.DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

        private const uint AbmGetState = 4, AbmSetState = 10;
        private const int AbsAutoHide = 1;

        private static AppBarData TaskbarData()
            => new() { Size = Interop.Marshal.SizeOf<AppBarData>(), Window = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null) };

        private static int GetTaskbarState()
        {
            var data = TaskbarData();
            return (int)SHAppBarMessage(AbmGetState, ref data);
        }

        private static void SetTaskbarAutoHide(bool on)
        {
            var data = TaskbarData();
            int state = (int)SHAppBarMessage(AbmGetState, ref data);
            data.Param = (IntPtr)(on ? state | AbsAutoHide : state & ~AbsAutoHide);
            SHAppBarMessage(AbmSetState, ref data);
        }

        /// <summary>
        /// „Taskleiste im Vollbild ausblenden“ ändert keine Windows-Einstellung mehr: Der maximierte Launcher deckt den ganzen
        /// Bildschirm ab (MaximizeHook, ApplyFullscreenFrame); Windows verdeckt die Taskleiste dann nur, solange der Launcher vorne ist.
        /// Auf dem Desktop und in anderen Programmen bleibt sie, wie du sie in Windows eingestellt hast.
        /// Früher hat der Launcher dafür „automatisch ausblenden“ in Windows eingeschaltet: Das wird hier einmalig zurückgestellt.
        /// </summary>
        private void UpdateFullscreenState()
        {
            if (windowTransition) return;

            try
            {
                if (taskbarAutoHideByUs || settings.TaskbarAutoHideByLauncher) RestoreTaskbar();
            }
            catch { }

            ApplyFullscreenFrame();
            UpdateTaskbarPeek();
        }

        /// <summary>
        /// Mit der eigenen Fensterleiste (WindowChrome) erkennt Windows 11 das maximierte Fenster sonst nicht als Vollbild
        /// und lässt die Taskleiste davor. Mit GlassFrameThickness = -1 klappt es: Dann verdeckt Windows die Taskleiste,
        /// solange der Launcher vorne ist, und zeigt sie sofort wieder, wenn du in ein anderes Programm wechselst.
        /// Nur im maximierten Zustand mit eingeschalteter Option, sonst bleibt die Leiste wie gehabt.
        /// </summary>
        private void ApplyFullscreenFrame()
        {
            try
            {
                if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is not { } chrome) return;
                bool full = settings.TaskbarHideMaximized && !controllerMode && WindowState == WindowState.Maximized;
                var wanted = new Thickness(full ? -1 : 0);
                if (chrome.GlassFrameThickness == wanted) return;

                var copy = (System.Windows.Shell.WindowChrome)chrome.Clone();
                copy.GlassFrameThickness = wanted;
                System.Windows.Shell.WindowChrome.SetWindowChrome(this, copy);
            }
            catch { }
        }

        // ───────── Maus an den Rand der Taskleiste: Taskleiste kurz hervorholen (wie bei „automatisch ausblenden“) ─────────

        private readonly System.Windows.Threading.DispatcherTimer taskbarPeekTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private DateTime taskbarEdgeSince = DateTime.MaxValue;
        private bool taskbarPeekHooked;

        private const uint AbmGetTaskbarPos = 5;

        private bool FullscreenActive()
            => settings.TaskbarHideMaximized && !controllerMode && IsVisible && WindowState == WindowState.Maximized
               && (IsActive || OwnedWindows.Cast<Window>().Any(w => w.IsActive));   // eigene Dialoge zählen mit

        private void UpdateTaskbarPeek()
        {
            if (!taskbarPeekHooked)
            {
                taskbarPeekHooked = true;
                taskbarPeekTimer.Tick += (s, e) => CheckTaskbarPeek();
                Activated += (s, e) => UpdateTaskbarPeek();
                // Kurz warten: Öffnet sich ein eigener Dialog, ist er erst einen Moment später aktiv
                Deactivated += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateTaskbarPeek), System.Windows.Threading.DispatcherPriority.Background);
            }

            // Auch hier (nicht nur bei Zustandswechseln): nach dem Controller-Modus läuft der Wechsel als Zwischenschritt
            if (!windowTransition) ApplyFullscreenFrame();

            bool active = FullscreenActive();
            if (active) taskbarPeekTimer.Start();
            else
            {
                taskbarPeekTimer.Stop();
                taskbarEdgeSince = DateTime.MaxValue;
            }
        }

        private void CheckTaskbarPeek()
        {
            if (!FullscreenActive())
            {
                UpdateTaskbarPeek();
                return;
            }

            try
            {
                var data = TaskbarData();
                if (data.Window == IntPtr.Zero || SHAppBarMessage(AbmGetTaskbarPos, ref data) == UIntPtr.Zero || !GetCursorPos(out var cursor)) return;

                // Liegt die Taskleiste auf dem Bildschirm des Launchers, und steht die Maus ganz an ihrem Rand?
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info)) return;

                var r = data.Rect;
                var m = info.Monitor;
                bool sameMonitor = r.Left >= m.Left - 2 && r.Right <= m.Right + 2 && r.Top >= m.Top - 2 && r.Bottom <= m.Bottom + 2;
                bool inside = cursor.X >= m.Left && cursor.X < m.Right && cursor.Y >= m.Top && cursor.Y < m.Bottom;
                bool atEdge = data.Edge switch
                {
                    0 => cursor.X <= m.Left + 1,     // links
                    1 => cursor.Y <= m.Top + 1,      // oben
                    2 => cursor.X >= m.Right - 2,    // rechts
                    _ => cursor.Y >= m.Bottom - 2    // unten
                };

                if (!sameMonitor || !inside || !atEdge)
                {
                    taskbarEdgeSince = DateTime.MaxValue;
                    return;
                }

                if (taskbarEdgeSince == DateTime.MaxValue) taskbarEdgeSince = DateTime.Now;
                if ((DateTime.Now - taskbarEdgeSince).TotalMilliseconds < 250) return;   // kurz verweilen, damit es nicht aus Versehen passiert

                // Taskleiste nach vorne holen; ein Klick zurück in den Launcher legt sie wieder dahinter
                taskbarEdgeSince = DateTime.MaxValue;
                NativeFeatures.SetForegroundWindow(data.Window);
            }
            catch { }
        }

        // ───────── Schwebende Leiste unten: nie unter der Taskleiste ─────────

        private const int WmSettingChange = 0x001A;
        private const double DockBaseMargin = 18;      // Abstand der Leiste zum Fensterrand (wie in MainWindow.xaml)
        private const double ContentBottomMargin = 104; // Platz für die Leiste unter dem Inhalt
        private double dockTaskbarLift;                 // so weit ragt die Taskleiste gerade ins Fenster (in WPF-Einheiten)
        private bool dockLiftHooked;
        private WindowState lastShownState = WindowState.Normal;

        private void InitDockTaskbarLift()
        {
            if (dockLiftHooked) return;
            dockLiftHooked = true;

            // Für ShowFromTray: Zustand vor dem Minimieren merken
            lastShownState = WindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            StateChanged += (s, e) =>
            {
                if (WindowState != WindowState.Minimized && !controllerMode) lastShownState = WindowState;
            };

            LocationChanged += (s, e) => UpdateDockTaskbarLift();
            SizeChanged += (s, e) => UpdateDockTaskbarLift();
            StateChanged += (s, e) => UpdateDockTaskbarLift();
            IsVisibleChanged += (s, e) => UpdateDockTaskbarLift();
            Activated += (s, e) => UpdateDockTaskbarLift();
            // Nach dem Wechsel in ein anderes Fenster (oder zur hervorgeholten Taskleiste) kurz warten, bis Windows sie zeigt
            Deactivated += (s, e) => Dispatcher.BeginInvoke(new Action(UpdateDockTaskbarLift), System.Windows.Threading.DispatcherPriority.Background);
            Loaded += (s, e) => UpdateDockTaskbarLift();
        }

        /// <summary>
        /// Wie weit liegt die Taskleiste gerade über dem unteren Fensterrand? 0, wenn sie nicht unten ist, das Fenster nicht
        /// berührt, im Vollbild hinter dem Launcher liegt oder du sie in Windows selbst automatisch ausblenden lässt.
        /// </summary>
        private double TaskbarOverlap()
        {
            if (!IsBottomNav || !IsVisible || WindowState == WindowState.Minimized || controllerMode) return 0;
            if (FullscreenActive()) return 0;   // Vollbild und vorne: Windows legt die Taskleiste dahinter

            try
            {
                var data = TaskbarData();
                if (data.Window == IntPtr.Zero || SHAppBarMessage(AbmGetTaskbarPos, ref data) == UIntPtr.Zero) return 0;
                if (data.Edge != 3) return 0;                              // Taskleiste nicht unten
                if ((GetTaskbarState() & AbsAutoHide) != 0) return 0;      // eigene Windows-Einstellung: fährt nur kurz hoch

                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero || !NativeExtras.GetWindowRect(handle, out var window)) return 0;

                var bar = data.Rect;
                bool horizontal = window.Right > bar.Left && window.Left < bar.Right;
                int overlap = Math.Min(window.Bottom, bar.Bottom) - Math.Max(window.Top, bar.Top);
                if (!horizontal || overlap <= 0 || window.Bottom <= bar.Top) return 0;

                return overlap / VisualTreeHelper.GetDpi(this).DpiScaleY;
            }
            catch
            {
                return 0;
            }
        }

        private void UpdateDockTaskbarLift()
        {
            if (NavDock == null || ContentArea == null) return;

            double lift = Math.Round(TaskbarOverlap());
            if (Math.Abs(lift - dockTaskbarLift) < 1) return;
            dockTaskbarLift = lift;

            var target = new Thickness(0, 0, 0, DockBaseMargin + lift);
            if (settings.PageAnimations && !settings.PerformanceMode)
            {
                NavDock.BeginAnimation(FrameworkElement.MarginProperty, new ThicknessAnimation(target, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                NavDock.BeginAnimation(FrameworkElement.MarginProperty, null);
                NavDock.Margin = target;
            }

            if (IsBottomNav) ContentArea.Margin = new Thickness(28, 42, 28, ContentBottomMargin + lift);
        }

        private void RestoreTaskbar()
        {
            try
            {
                if (!taskbarAutoHideByUs && !settings.TaskbarAutoHideByLauncher) return;
                SetTaskbarAutoHide(false);
                taskbarAutoHideByUs = false;
                settings.TaskbarAutoHideByLauncher = false;
                SaveSettings();
            }
            catch { }
        }

        /// <summary>Nach dem Umschalten ändert sich der freie Bereich: das maximierte Fenster daran anpassen.</summary>
        private async Task RefitMaximizedAsync()
        {
            if (refitRunning) return;
            refitRunning = true;
            try
            {
                await Task.Delay(400);
                if (WindowState != WindowState.Maximized || controllerMode || windowTransition) return;

                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (!GetMonitorInfo(monitor, ref info) || !NativeExtras.GetWindowRect(handle, out var rect)) return;
                if (rect.Bottom - rect.Top != info.Work.Bottom - info.Work.Top || rect.Right - rect.Left != info.Work.Right - info.Work.Left)
                    ReapplyMaximize();   // höchstens einmal je Umschalten, nie in einer Schleife
            }
            finally
            {
                refitRunning = false;
            }
        }

#if DEBUG
        /// <summary>Entwickler-Test: prüft die obersten Pixelreihen des Fensters auf eine helle Linie und nennt die Elemente dort.</summary>
        private void DevTopLineReport()
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (!NativeExtras.GetWindowRect(handle, out var rect)) return;
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(monitor, ref info);

            int left = Math.Max(rect.Left, info.Monitor.Left), right = Math.Min(rect.Right, info.Monitor.Right);
            int top = Math.Max(rect.Top, info.Monitor.Top);
            int width = Math.Max(1, right - left);
            var dpi = VisualTreeHelper.GetDpi(this);

            var text = new System.Text.StringBuilder();
            text.AppendLine($"Zustand: {WindowState}, Rahmen: {WindowStyle}, DPI: {dpi.DpiScaleX:0.##}");
            text.AppendLine($"Fenster: {rect.Left},{rect.Top} – {rect.Right},{rect.Bottom}");
            text.AppendLine($"Bildschirm: {info.Monitor.Left},{info.Monitor.Top} – {info.Monitor.Right},{info.Monitor.Bottom} · Arbeitsbereich bis {info.Work.Bottom}");
            text.AppendLine($"Rand-Ausgleich: {WindowRoot.Margin}");

            using (var bitmap = new System.Drawing.Bitmap(width, 4))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, 4));

                for (int y = 0; y < 4; y++)
                {
                    int bright = 0, first = -1, last = -1;
                    System.Drawing.Color sample = System.Drawing.Color.Empty;
                    for (int x = 0; x < width; x += 3)
                    {
                        var c = bitmap.GetPixel(x, y);
                        if (c.R + c.G + c.B > 330)
                        {
                            bright++;
                            if (first < 0) { first = x; sample = c; }
                            last = x;
                        }
                    }
                    text.AppendLine(bright == 0
                        ? $"Reihe {y}: keine helle Linie"
                        : $"Reihe {y}: HELL von x={first} bis x={last} (Farbe {sample.R},{sample.G},{sample.B})");
                }
            }

            // Elemente, die ganz oben liegen
            text.AppendLine("Elemente oben:");
            int listed = 0;
            void Walk(DependencyObject node)
            {
                if (listed >= 25) return;
                if (node is FrameworkElement element && element.IsVisible && element.ActualHeight > 0 && element.Name.Length > 0)
                {
                    try
                    {
                        var bounds = element.TransformToAncestor(this).TransformBounds(new Rect(element.RenderSize));
                        if (bounds.Top <= 2 && bounds.Bottom > 0)
                        {
                            text.AppendLine($"  {element.Name} ({element.GetType().Name}) {bounds.Left:0},{bounds.Top:0.##} {bounds.Width:0}×{bounds.Height:0}");
                            listed++;
                        }
                    }
                    catch { }
                }
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
            }
            Walk(WindowRoot);

            try { System.Windows.Clipboard.SetText(text.ToString()); } catch { }
            Msg(text + "\n(In die Zwischenablage kopiert)", "Linie oben prüfen");
        }
#endif

        private void ChkHideTaskbar_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.TaskbarHideMaximized = ChkHideTaskbar.IsChecked == true;
            SaveSettings();
            UpdateFullscreenState();
            ReapplyMaximize();   // neue Größe sofort: ganzer Bildschirm oder Arbeitsbereich
        }

        /// <summary>Ein schon maximiertes Fenster neu maximieren, damit die neue Größe gilt.</summary>
        private void ReapplyMaximize()
        {
            if (WindowState == WindowState.Maximized && !controllerMode)
            {
                syncingMaximize = true;
                windowTransition = true;
                try
                {
                    WindowState = WindowState.Normal;
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                }
                finally
                {
                    syncingMaximize = false;
                    windowTransition = false;
                }
            }
            UpdateFullscreenState();
        }
    }
}
