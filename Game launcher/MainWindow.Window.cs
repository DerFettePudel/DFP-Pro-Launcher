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
            if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

                bool full = controllerMode;   // Taskleiste ausblenden läuft über „automatisch ausblenden“, dann ist der Arbeitsbereich ohnehin der ganze Bildschirm
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
        /// Ist der Launcher maximiert und die Option an, schaltet er in Windows „Taskleiste automatisch ausblenden“ ein:
        /// Das Fenster nutzt den ganzen Bildschirm, die Taskleiste erscheint, wenn die Maus an den Rand fährt.
        /// Beim Verkleinern, Minimieren oder Beenden stellt er die eigene Einstellung des Nutzers wieder her.
        /// </summary>
        private void UpdateFullscreenState()
        {
            bool want = settings.TaskbarHideMaximized && WindowState == WindowState.Maximized && !controllerMode && IsVisible;

            try
            {
                if (want && !taskbarAutoHideByUs)
                {
                    if ((GetTaskbarState() & AbsAutoHide) != 0) return;   // hatte der Nutzer schon selbst so eingestellt
                    SetTaskbarAutoHide(true);
                    taskbarAutoHideByUs = true;
                    settings.TaskbarAutoHideByLauncher = true;   // falls der Launcher abstürzt: beim nächsten Start zurückstellen
                    SaveSettings();
                    _ = RefitMaximizedAsync();
                }
                else if (!want && taskbarAutoHideByUs)
                {
                    RestoreTaskbar();
                }
            }
            catch { }
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
            await Task.Delay(400);
            if (WindowState != WindowState.Maximized || controllerMode) return;

            var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info) || !NativeExtras.GetWindowRect(handle, out var rect)) return;
            if (rect.Bottom - rect.Top != info.Work.Bottom - info.Work.Top || rect.Right - rect.Left != info.Work.Right - info.Work.Left)
                ReapplyMaximize();
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
        }

        /// <summary>Ein schon maximiertes Fenster neu maximieren, damit die neue Größe gilt.</summary>
        private void ReapplyMaximize()
        {
            if (WindowState == WindowState.Maximized && !controllerMode)
            {
                syncingMaximize = true;
                try
                {
                    WindowState = WindowState.Normal;
                    WindowStyle = WindowStyle.None;
                    WindowState = WindowState.Maximized;
                }
                finally
                {
                    syncingMaximize = false;
                }
            }
            UpdateFullscreenState();
        }
    }
}
