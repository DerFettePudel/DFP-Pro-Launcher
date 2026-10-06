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
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
        internal struct ShQueryRbInfo
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        internal static extern int SHQueryRecycleBin(string? pszRootPath, ref ShQueryRbInfo pSHQueryRBInfo);
    }

    internal static class NativeFeatures
    {
        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct Margins
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [Interop.DllImport("dwmapi.dll")]
        internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [Interop.DllImport("dwmapi.dll")]
        internal static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

        [Interop.DllImport("user32.dll")]
        internal static extern int GetWindowLong(IntPtr hwnd, int index);

        [Interop.DllImport("user32.dll")]
        internal static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [Interop.DllImport("xinput1_4.dll")]
        internal static extern uint XInputGetState(uint index, out XInputState state);

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct XInputBatteryInformation
        {
            public byte BatteryType;
            public byte BatteryLevel;
        }

        [Interop.DllImport("xinput1_4.dll")]
        internal static extern uint XInputGetBatteryInformation(uint index, byte devType, out XInputBatteryInformation info);

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct XInputVibration
        {
            public ushort LeftMotor;
            public ushort RightMotor;
        }

        [Interop.DllImport("xinput1_4.dll")]
        internal static extern uint XInputSetState(uint index, ref XInputVibration vibration);

        [Interop.DllImport("winmm.dll")]
        internal static extern uint timeBeginPeriod(uint milliseconds);

        [Interop.DllImport("winmm.dll")]
        internal static extern uint timeEndPeriod(uint milliseconds);

        [Interop.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [Interop.DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [Interop.DllImport("kernel32.dll", SetLastError = true)]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        [Interop.DllImport("kernel32.dll", CharSet = Interop.CharSet.Unicode, SetLastError = true)]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder text, ref int size);

        /// <summary>Liest den Dateipfad eines laufenden Prozesses (funktioniert auch bei geschützten Spielen).</summary>
        internal static string? GetProcessPath(int pid)
        {
            IntPtr handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (handle == IntPtr.Zero) return null;

            try
            {
                var buffer = new System.Text.StringBuilder(1024);
                int size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }
    }

    internal static class NativeExtras
    {
        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [Interop.DllImport("user32.dll", EntryPoint = "SystemParametersInfo", SetLastError = true)]
        internal static extern bool SystemParametersInfoMouse(uint uiAction, uint uiParam, int[] pvParam, uint fWinIni);

        [Interop.StructLayout(Interop.LayoutKind.Sequential, CharSet = Interop.CharSet.Unicode)]
        internal struct DisplayDevice
        {
            public int cb;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential, CharSet = Interop.CharSet.Unicode)]
        internal struct DisplayMode
        {
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct LastInputInfo
        {
            public uint cbSize;
            public uint dwTime;
        }

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool GetLastInputInfo(ref LastInputInfo plii);

        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [Interop.DllImport("user32.dll")]
        internal static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

        [Interop.DllImport("user32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

        [Interop.DllImport("user32.dll", CharSet = Interop.CharSet.Unicode)]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool EnumDisplaySettings(string? lpszDeviceName, int iModeNum, ref DisplayMode lpDevMode);
    }

    internal static class ShellNative
    {
        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct NativeSize
        {
            public int cx;
            public int cy;
        }

        [Interop.ComImport]
        [Interop.Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [Interop.InterfaceType(Interop.ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItemImageFactory
        {
            [Interop.PreserveSig]
            int GetImage([Interop.In, Interop.MarshalAs(Interop.UnmanagedType.Struct)] NativeSize size, [Interop.In] int flags, out IntPtr phbm);
        }

        [Interop.DllImport("shell32.dll", CharSet = Interop.CharSet.Unicode, PreserveSig = false)]
        internal static extern void SHCreateItemFromParsingName(
            [Interop.MarshalAs(Interop.UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid,
            [Interop.MarshalAs(Interop.UnmanagedType.Interface)] out IShellItemImageFactory ppv);

        [Interop.DllImport("gdi32.dll")]
        [return: Interop.MarshalAs(Interop.UnmanagedType.Bool)]
        internal static extern bool DeleteObject(IntPtr hObject);
    }

    /// <summary>Anbindung an die Windows-Joystick-Schnittstelle (PlayStation-, Switch- und generische Gamepads).</summary>
    internal static class NativePad
    {
        [Interop.StructLayout(Interop.LayoutKind.Sequential)]
        internal struct JoyInfoEx
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [Interop.StructLayout(Interop.LayoutKind.Sequential, CharSet = Interop.CharSet.Unicode)]
        internal struct JoyCaps
        {
            public ushort wMid;
            public ushort wPid;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax;
            public uint wNumButtons, wPeriodMin, wPeriodMax;
            public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax;
            public uint wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;
            [Interop.MarshalAs(Interop.UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [Interop.DllImport("winmm.dll")]
        internal static extern uint joyGetNumDevs();

        [Interop.DllImport("winmm.dll")]
        internal static extern uint joyGetPosEx(uint uJoyID, ref JoyInfoEx pji);

        [Interop.DllImport("winmm.dll", CharSet = Interop.CharSet.Unicode, EntryPoint = "joyGetDevCapsW")]
        internal static extern uint joyGetDevCapsW(uint uJoyID, ref JoyCaps pjc, uint cbjc);
    }
}
