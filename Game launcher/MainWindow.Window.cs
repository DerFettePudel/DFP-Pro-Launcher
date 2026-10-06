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
        private const uint MonitorDefaultToNearest = 2;
        private static readonly IntPtr HwndTopmost = new(-1);
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpShowWindow = 0x0040;

        private readonly DispatcherTimer taskbarTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
        private bool taskbarRevealed;
        private int taskbarEdgeTicks;

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

            taskbarTimer.Tick += (s, e) => CheckTaskbarReveal();
            StateChanged += (s, e) => UpdateFullscreenState();
            Activated += (s, e) => UpdateFullscreenState();
            Deactivated += (s, e) => UpdateFullscreenState();
        }

        private bool TaskbarHidingActive => settings.TaskbarHideMaximized && WindowState == WindowState.Maximized && !controllerMode;

        /// <summary>
        /// Legt die Größe beim Maximieren selbst fest: genau der Arbeitsbereich (ohne Überstand über den Bildschirmrand,
        /// der sonst als dünne Linie oben sichtbar werden kann) oder, im Vollbild, der ganze Bildschirm.
        /// </summary>
        private IntPtr MaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WmGetMinMaxInfo) return IntPtr.Zero;

            try
            {
                IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;

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

        /// <summary>Im Vollbild bleibt der aktive Launcher vor der Taskleiste; die Überwachung des Rands läuft nur dann.</summary>
        private void UpdateFullscreenState()
        {
            bool hiding = TaskbarHidingActive && IsActive;
            taskbarRevealed = false;
            taskbarEdgeTicks = 0;

            if (!controllerMode) Topmost = hiding || settings.AlwaysOnTop;

            if (hiding && !taskbarTimer.IsEnabled) taskbarTimer.Start();
            else if (!hiding && taskbarTimer.IsEnabled) taskbarTimer.Stop();
        }

        /// <summary>Zeigt die Taskleiste, wenn die Maus an ihrem Rand steht, und blendet sie wieder aus, sobald die Maus weg ist.</summary>
        private void CheckTaskbarReveal()
        {
            if (!TaskbarHidingActive || !IsActive)
            {
                taskbarTimer.Stop();
                return;
            }

            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                IntPtr monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
                var info = new MonitorInfo { Size = Interop.Marshal.SizeOf<MonitorInfo>() };
                if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info) || !GetCursorPos(out var cursor)) return;

                var m = info.Monitor;
                var w = info.Work;
                if (cursor.X < m.Left || cursor.X >= m.Right || cursor.Y < m.Top || cursor.Y >= m.Bottom) return;

                // An welchem Rand steht die Taskleiste? (Unterschied zwischen Bildschirm und Arbeitsbereich)
                int bottom = m.Bottom - w.Bottom, top = w.Top - m.Top, left = w.Left - m.Left, right = m.Right - w.Right;
                int thickness = Math.Max(Math.Max(bottom, top), Math.Max(left, right));
                if (thickness <= 0) return;   // Taskleiste blendet sich schon selbst aus

                int distance = bottom == thickness ? m.Bottom - 1 - cursor.Y
                             : top == thickness ? cursor.Y - m.Top
                             : left == thickness ? cursor.X - m.Left
                             : m.Right - 1 - cursor.X;

                if (!taskbarRevealed)
                {
                    taskbarEdgeTicks = distance <= 1 ? taskbarEdgeTicks + 1 : 0;
                    if (taskbarEdgeTicks < 2) return;   // kurz verweilen, damit sie nicht beim Vorbeifahren aufspringt

                    IntPtr tray = FindTaskbar(m);
                    if (tray == IntPtr.Zero) return;
                    NativeExtras.SetWindowPos(tray, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
                    taskbarRevealed = true;
                }
                else if (distance > thickness + 16)
                {
                    // Maus hat die Taskleiste verlassen: Launcher wieder davor
                    NativeExtras.SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
                    taskbarRevealed = false;
                    taskbarEdgeTicks = 0;
                }
            }
            catch { }
        }

        /// <summary>Taskleiste des Bildschirms finden (Hauptbildschirm und weitere Bildschirme haben eigene Fenster).</summary>
        private static IntPtr FindTaskbar(NativeExtras.NativeRect monitor)
        {
            foreach (string className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
            {
                IntPtr window = IntPtr.Zero;
                while ((window = FindWindowEx(IntPtr.Zero, window, className, null)) != IntPtr.Zero)
                {
                    if (!NativeExtras.GetWindowRect(window, out var rect)) continue;
                    int centerX = (rect.Left + rect.Right) / 2, centerY = (rect.Top + rect.Bottom) / 2;
                    if (centerX >= monitor.Left && centerX < monitor.Right && centerY >= monitor.Top && centerY < monitor.Bottom) return window;
                }
            }
            return IntPtr.Zero;
        }

        private void ChkHideTaskbar_Changed(object sender, RoutedEventArgs e)
        {
            if (isLoadingSettings) return;
            settings.TaskbarHideMaximized = ChkHideTaskbar.IsChecked == true;
            SaveSettings();
            ReapplyMaximize();
        }

        /// <summary>Ein schon maximiertes Fenster neu maximieren, damit die neue Größe gilt.</summary>
        private void ReapplyMaximize()
        {
            if (WindowState == WindowState.Maximized && !controllerMode)
            {
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
            }
            UpdateFullscreenState();
        }
    }
}
