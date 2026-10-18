using System;
using System.Collections.Generic;
using System.Linq;
using Interop = System.Runtime.InteropServices;

namespace Game_launcher
{
    /// <summary>Auflösung, Bildrate und HDR des Hauptbildschirms lesen und umschalten (für Einstellungen pro Spiel).</summary>
    internal static class DisplayModes
    {
        internal readonly record struct Mode(int Width, int Height, int Hz)
        {
            public override string ToString() => $"{Width}x{Height}@{Hz}";

            public static Mode? Parse(string? text)
            {
                var match = System.Text.RegularExpressions.Regex.Match(text ?? string.Empty, @"^(\d+)x(\d+)@(\d+)$");
                if (!match.Success) return null;
                return new Mode(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
            }
        }

        // ───────── Auflösung und Bildrate ─────────

        [Interop.StructLayout(Interop.LayoutKind.Sequential, CharSet = Interop.CharSet.Unicode)]
        private struct DevMode
        {
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            public short SpecVersion, DriverVersion, Size, DriverExtra;
            public int Fields;
            public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
            public short Color, Duplex, YResolution, TTOption, Collate;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
            public short LogPixels;
            public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
            public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
        }

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string? deviceName, ref DevMode devMode, IntPtr hwnd, int flags, IntPtr param);

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        private static extern int ChangeDisplaySettingsEx(string? deviceName, IntPtr devMode, IntPtr hwnd, int flags, IntPtr param);

        private const int EnumCurrentSettings = -1;
        private const int CdsFullscreen = 0x4;    // nur vorübergehend, nicht in der Registrierung speichern
        private const int DmPelsWidth = 0x80000, DmPelsHeight = 0x100000, DmDisplayFrequency = 0x400000;

        private static DevMode NewDevMode() => new() { DeviceName = string.Empty, FormName = string.Empty, Size = (short)Interop.Marshal.SizeOf<DevMode>() };

        public static Mode? Current()
        {
            var mode = NewDevMode();
            return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) ? new Mode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency) : null;
        }

        /// <summary>Alle Modi des Hauptbildschirms, größte zuerst (nur 32 Bit Farbtiefe, ohne Doppelte).</summary>
        public static List<Mode> All()
        {
            var list = new HashSet<Mode>();
            var mode = NewDevMode();
            for (int i = 0; EnumDisplaySettings(null, i, ref mode); i++)
            {
                if (mode.BitsPerPel == 32 && mode.PelsWidth >= 800) list.Add(new Mode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency));
            }
            return list.OrderByDescending(m => m.Width * m.Height).ThenByDescending(m => m.Hz).ToList();
        }

        public static bool Apply(Mode target)
        {
            var mode = NewDevMode();
            mode.PelsWidth = target.Width;
            mode.PelsHeight = target.Height;
            mode.DisplayFrequency = target.Hz;
            mode.Fields = DmPelsWidth | DmPelsHeight | DmDisplayFrequency;
            return ChangeDisplaySettingsEx(null, ref mode, IntPtr.Zero, CdsFullscreen, IntPtr.Zero) == 0;
        }

        /// <summary>Zurück zum in Windows eingestellten Modus.</summary>
        public static void Reset() => ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

        // ───────── HDR (Windows: „HDR verwenden“) ─────────

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct Luid
        {
            public uint Low;
            public int High;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct PathInfo
        {
            public Luid SourceAdapter;
            public uint SourceId, SourceModeIndex, SourceFlags;
            public Luid TargetAdapter;
            public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLineOrdering;
            public int TargetAvailable;
            public uint TargetFlags, Flags;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential, Size = 64)]
        private struct ModeInfo
        {
            public uint InfoType;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct Header
        {
            public int Type, Size;
            public Luid Adapter;
            public uint Id;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential, CharSet = Interop.CharSet.Unicode)]
        private struct SourceName
        {
            public Header Header;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct ColorInfo
        {
            public Header Header;
            public uint Value, Encoding, BitsPerChannel;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct ColorState
        {
            public Header Header;
            public uint Value;
        }

        [Interop.DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

        [Interop.DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Interop.Out] PathInfo[] paths, ref uint modeCount, [Interop.Out] ModeInfo[] modes, IntPtr topology);

        [Interop.DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref SourceName info);

        [Interop.DllImport("user32.dll")]
        private static extern int DisplayConfigGetDeviceInfo(ref ColorInfo info);

        [Interop.DllImport("user32.dll")]
        private static extern int DisplayConfigSetDeviceInfo(ref ColorState info);

        private const uint QdcOnlyActivePaths = 2;
        private const int GetSourceName = 1, GetAdvancedColorInfo = 9, SetAdvancedColorState = 10;

        /// <summary>Anschluss des Hauptbildschirms für die HDR-Abfrage.</summary>
        private static (Luid Adapter, uint Id)? PrimaryTarget()
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return null;
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return null;

            string primary = System.Windows.Forms.Screen.PrimaryScreen?.DeviceName ?? string.Empty;
            for (int i = 0; i < pathCount; i++)
            {
                var name = new SourceName
                {
                    Header = new Header { Type = GetSourceName, Size = Interop.Marshal.SizeOf<SourceName>(), Adapter = paths[i].SourceAdapter, Id = paths[i].SourceId },
                    GdiName = string.Empty
                };
                if (DisplayConfigGetDeviceInfo(ref name) != 0) continue;
                if (string.Equals(name.GdiName, primary, StringComparison.OrdinalIgnoreCase)) return (paths[i].TargetAdapter, paths[i].TargetId);
            }
            return pathCount > 0 ? (paths[0].TargetAdapter, paths[0].TargetId) : null;
        }

        /// <summary>null = HDR wird nicht unterstützt (oder unbekannt), sonst ob HDR gerade an ist.</summary>
        public static bool? HdrEnabled()
        {
            try
            {
                if (PrimaryTarget() is not { } target) return null;
                var info = new ColorInfo { Header = new Header { Type = GetAdvancedColorInfo, Size = Interop.Marshal.SizeOf<ColorInfo>(), Adapter = target.Adapter, Id = target.Id } };
                if (DisplayConfigGetDeviceInfo(ref info) != 0) return null;
                bool supported = (info.Value & 1) != 0;
                return supported ? (info.Value & 2) != 0 : null;
            }
            catch
            {
                return null;
            }
        }

        public static bool SetHdr(bool on)
        {
            try
            {
                if (PrimaryTarget() is not { } target) return false;
                var state = new ColorState { Header = new Header { Type = SetAdvancedColorState, Size = Interop.Marshal.SizeOf<ColorState>(), Adapter = target.Adapter, Id = target.Id }, Value = on ? 1u : 0u };
                return DisplayConfigSetDeviceInfo(ref state) == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
