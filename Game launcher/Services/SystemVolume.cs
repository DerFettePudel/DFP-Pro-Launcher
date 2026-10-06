using System;
using System.Runtime.InteropServices;

namespace Game_launcher
{
    /// <summary>
    /// Windows-Hauptlautstärke über Core Audio (IAudioEndpointVolume), ohne zusätzliche Pakete.
    /// Bitte im Hintergrund aufrufen (Task.Run), nicht auf dem Oberflächen-Thread.
    /// </summary>
    internal static class SystemVolume
    {
        private const int ERender = 0;        // Wiedergabegeräte
        private const int EMultimedia = 1;    // Standardgerät für Multimedia
        private const int ClsCtxAll = 23;

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object endpointVolume);
        }

        [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
            [PreserveSig] int GetChannelCount(out uint count);
            [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
            [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
            [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
            [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
            [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
            [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
            [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
            [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        private static IAudioEndpointVolume? Open()
        {
            IMMDeviceEnumerator? enumerator = null;
            IMMDevice? device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                if (enumerator.GetDefaultAudioEndpoint(ERender, EMultimedia, out device) != 0 || device == null) return null;

                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, ClsCtxAll, IntPtr.Zero, out object volume) != 0) return null;
                return volume as IAudioEndpointVolume;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }

        /// <summary>Liest Lautstärke (0 bis 1) und Stummschaltung; null, wenn kein Wiedergabegerät erreichbar ist.</summary>
        public static (float Level, bool Muted)? Read()
        {
            var volume = Open();
            if (volume == null) return null;
            try
            {
                if (volume.GetMasterVolumeLevelScalar(out float level) != 0) return null;
                volume.GetMute(out bool muted);
                return (level, muted);
            }
            catch
            {
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }

        public static bool Write(float level, bool muted)
        {
            var volume = Open();
            if (volume == null) return false;
            try
            {
                var context = Guid.Empty;
                bool ok = volume.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref context) == 0;
                volume.SetMute(muted, ref context);
                return ok;
            }
            catch
            {
                return false;
            }
            finally
            {
                Marshal.ReleaseComObject(volume);
            }
        }
    }
}
