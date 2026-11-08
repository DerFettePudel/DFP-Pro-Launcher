using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Interop = System.Runtime.InteropServices;

namespace Game_launcher
{
    /// <summary>
    /// Akkustand von PlayStation-Controllern (DualSense, DualSense Edge, DualShock 4) direkt aus den HID-Daten.
    /// Nur lesen, nie etwas an den Controller senden: Über Bluetooth liefert der Controller den Akku daher nur,
    /// wenn ein anderes Programm (z. B. Steam) ihn schon in den erweiterten Modus geschaltet hat. Über Kabel immer.
    /// Blockiert bis etwa eine Sekunde, darum nur im Hintergrund aufrufen.
    /// </summary>
    internal static class PlayStationBattery
    {
        internal readonly record struct Reading(int Percent, bool Wired, bool Charging);

        private const ushort SonyVendor = 0x054C;
        private static readonly HashSet<ushort> DualShock4 = new() { 0x05C4, 0x09CC, 0x0BA0 };
        private static readonly HashSet<ushort> DualSense = new() { 0x0CE6, 0x0DF2 };
        private const string BluetoothHid = "00001124-0000-1000-8000-00805f9b34fb";

        public static Reading? Read()
        {
            try
            {
                foreach (string path in SonyDevicePaths())
                {
                    var reading = ReadDevice(path);
                    if (reading != null) return reading;
                }
            }
            catch { }
            return null;
        }

        // ───────── Geräte finden ─────────

        private static List<string> SonyDevicePaths()
        {
            var result = new List<string>();
            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
            if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

            try
            {
                var data = new DeviceInterfaceData { Size = Interop.Marshal.SizeOf<DeviceInterfaceData>() };
                for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
                {
                    SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out int needed, IntPtr.Zero);
                    if (needed <= 0) continue;

                    IntPtr detail = Interop.Marshal.AllocHGlobal(needed);
                    try
                    {
                        // cbSize der Struktur SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 bei 64 Bit, 6 bei 32 Bit
                        Interop.Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, needed, out _, IntPtr.Zero)) continue;
                        string? path = Interop.Marshal.PtrToStringUni(detail + 4);
                        if (path != null && path.Contains("054c", StringComparison.OrdinalIgnoreCase)) result.Add(path);
                    }
                    finally
                    {
                        Interop.Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            return result;
        }

        // ───────── Eingabebericht lesen und auswerten ─────────

        private static Reading? ReadDevice(string path)
        {
            using SafeFileHandle handle = CreateFile(path, GenericRead, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            var attributes = new HiddAttributes { Size = Interop.Marshal.SizeOf<HiddAttributes>() };
            if (!HidD_GetAttributes(handle, ref attributes) || attributes.VendorId != SonyVendor) return null;
            bool isDualSense = DualSense.Contains(attributes.ProductId);
            if (!isDualSense && !DualShock4.Contains(attributes.ProductId)) return null;

            // Nur die Gamepad-Schnittstelle (Usage Page 1, Usage 5), Länge des Eingabeberichts aus den HID-Angaben
            if (!HidD_GetPreparsedData(handle, out IntPtr preparsed)) return null;
            int length;
            try
            {
                var caps = new byte[64];
                if (HidP_GetCaps(preparsed, caps) != HidpStatusSuccess) return null;
                ushort usage = BitConverter.ToUInt16(caps, 0), usagePage = BitConverter.ToUInt16(caps, 2);
                if (usagePage != 0x01 || usage != 0x05) return null;
                length = BitConverter.ToUInt16(caps, 4);
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
            if (length < 33) return null;

            bool bluetooth = path.Contains(BluetoothHid, StringComparison.OrdinalIgnoreCase);
            IntPtr buffer = Interop.Marshal.AllocHGlobal(length);
            IntPtr overlapped = Interop.Marshal.AllocHGlobal(Interop.Marshal.SizeOf<NativeOverlapped>());
            using var signal = new ManualResetEvent(false);
            try
            {
                var report = new byte[length];
                // Ein paar Berichte probieren: Der erste nach dem Öffnen kann ein anderer Berichtstyp sein
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    signal.Reset();
                    Interop.Marshal.StructureToPtr(new NativeOverlapped { EventHandle = signal.SafeWaitHandle.DangerousGetHandle() }, overlapped, false);

                    if (!ReadFile(handle, buffer, length, IntPtr.Zero, overlapped) && Interop.Marshal.GetLastWin32Error() != ErrorIoPending) return null;
                    if (!signal.WaitOne(400))
                    {
                        // Keine Daten (z. B. Controller am Funk-Adapter aus): Lesen abbrechen und auf das Ende warten
                        CancelIoEx(handle, overlapped);
                        GetOverlappedResult(handle, overlapped, out _, true);
                        return null;
                    }
                    if (!GetOverlappedResult(handle, overlapped, out int read, false) || read < 33) return null;

                    Interop.Marshal.Copy(buffer, report, 0, Math.Min(read, length));
                    var reading = isDualSense ? ParseDualSense(report, read, bluetooth) : ParseDualShock4(report, read, bluetooth);
                    if (reading != null) return reading;
                }
                return null;
            }
            finally
            {
                Interop.Marshal.FreeHGlobal(overlapped);
                Interop.Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>DualSense: Statusbyte mit Akkustand (untere 4 Bit, 0 bis 10) und Ladezustand (obere 4 Bit).</summary>
        private static Reading? ParseDualSense(byte[] report, int read, bool bluetooth)
        {
            int offset;
            if (!bluetooth && report[0] == 0x01) offset = 53;          // USB
            else if (bluetooth && report[0] == 0x31) offset = 54;      // Bluetooth, erweiterter Modus
            else return null;                                          // Bluetooth im einfachen Modus: kein Akku
            if (read <= offset) return null;

            int status = report[offset];
            int level = status & 0x0F, state = status >> 4;
            return state switch
            {
                0 => new Reading(Math.Min(level * 10 + 5, 100), !bluetooth, false),   // entlädt
                1 => new Reading(Math.Min(level * 10 + 5, 100), !bluetooth, true),    // lädt
                2 => new Reading(100, !bluetooth, false),                            // voll geladen
                _ => null                                                            // Fehler, zu heiß/kalt
            };
        }

        /// <summary>DualShock 4: Statusbyte mit Akkustand (untere 4 Bit) und Kabel (Bit 4).</summary>
        private static Reading? ParseDualShock4(byte[] report, int read, bool bluetooth)
        {
            int offset;
            if (!bluetooth && report[0] == 0x01) offset = 30;          // USB und Funk-Adapter
            else if (bluetooth && report[0] == 0x11) offset = 32;      // Bluetooth, erweiterter Modus
            else return null;
            if (read <= offset) return null;

            int status = report[offset];
            int level = status & 0x0F;
            bool cable = (status & 0x10) != 0;
            if (!cable) return new Reading(Math.Min(level * 10 + 5, 100), false, false);
            return level switch
            {
                < 10 => new Reading(level * 10 + 5, true, true),
                10 => new Reading(100, true, true),
                11 => new Reading(100, true, false),
                _ => null
            };
        }

        // ───────── Windows-Schnittstellen ─────────

        private const int DigcfPresent = 0x2, DigcfDeviceInterface = 0x10;
        private const uint GenericRead = 0x80000000;
        private const uint FileShareRead = 0x1, FileShareWrite = 0x2, OpenExisting = 3, FileFlagOverlapped = 0x40000000;
        private const int ErrorIoPending = 997;
        private const int HidpStatusSuccess = 0x00110000;

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct DeviceInterfaceData
        {
            public int Size;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        private struct HiddAttributes
        {
            public int Size;
            public ushort VendorId, ProductId, VersionNumber;
        }

        [Interop.DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid guid);

        [Interop.DllImport("hid.dll")]
        private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

        [Interop.DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsed);

        [Interop.DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsed);

        [Interop.DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsed, byte[] caps);

        [Interop.DllImport("setupapi.dll", CharSet = Interop.CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr parent, int flags);

        [Interop.DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr deviceInfo, ref Guid classGuid, uint index, ref DeviceInterfaceData data);

        [Interop.DllImport("setupapi.dll", CharSet = Interop.CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref DeviceInterfaceData data, IntPtr detail, int detailSize, out int requiredSize, IntPtr deviceInfo);

        [Interop.DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [Interop.DllImport("kernel32.dll", CharSet = Interop.CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [Interop.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int count, IntPtr read, IntPtr overlapped);

        [Interop.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int transferred, bool wait);

        [Interop.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }
}
