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
    /// <summary>Spielt animierte GIFs ab (WPF kann das nicht von Haus aus). Teilbilder werden zusammengesetzt.</summary>
    public sealed class GifAnimator
    {
        /// <summary>Warum die letzte Animation nicht geladen werden konnte (für das Fehlerprotokoll).</summary>
        public static string? LastError { get; private set; }

        private readonly List<BitmapSource> frames = new();
        private readonly List<int> delays = new();
        private DispatcherTimer? timer;
        private System.Windows.Controls.Image? target;
        private int index;

        public BitmapSource? FirstFrame => frames.Count > 0 ? frames[0] : null;

        /// <summary>Liest die Anzeigedauer eines Einzelbilds (ms). Die Formate nennen die Eigenschaft unterschiedlich, daher per Reflexion.</summary>
        private static int ReadDelayMs(object frameMetadata, string extension)
        {
            try
            {
                string method = extension == ".gif" ? "GetGifMetadata" : extension == ".webp" ? "GetWebpMetadata" : "GetPngMetadata";
                var helper = typeof(SixLabors.ImageSharp.Image).Assembly.GetType("SixLabors.ImageSharp.MetadataExtensions");
                var getter = helper?.GetMethods().FirstOrDefault(m => m.Name == method
                    && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "ImageFrameMetadata");
                object? meta = getter?.Invoke(null, new[] { frameMetadata });
                if (meta == null) return 80;

                foreach (string name in new[] { "FrameDelay", "FrameDuration" })
                {
                    object? value = meta.GetType().GetProperty(name)?.GetValue(meta);
                    if (value == null) continue;

                    double number = value switch
                    {
                        int i => i,
                        uint u => u,
                        long l => l,
                        double d => d,
                        _ => Convert.ToDouble(value.GetType().GetMethod("ToDouble")?.Invoke(value, null) ?? 0.0)
                    };

                    double ms = extension == ".gif" ? number * 10 : extension == ".webp" ? number : number * 1000;
                    return (int)Math.Clamp(ms, 20, 5000);
                }
            }
            catch { }

            return 80;
        }

        public static GifAnimator? TryLoad(string path, int maxWidth)
        {
            try
            {
                LastError = null;
                if (new FileInfo(path).Length > 30L * 1024 * 1024)
                {
                    LastError = "Datei größer als 30 MB";
                    return null;
                }

                string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var options = new SixLabors.ImageSharp.Formats.DecoderOptions { MaxFrames = 150 };
                using var image = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.Rgba32>(options, path);

                int total = image.Frames.Count;
                if (total < 2)
                {
                    LastError = "Das Bild enthält keine Animation";
                    return null;
                }

                int stride = (int)Math.Ceiling(total / 90.0);   // höchstens etwa 90 Bilder im Speicher
                int width = Math.Max(1, Math.Min(maxWidth, image.Width));
                var animator = new GifAnimator();
                int pendingDelay = 0;

                for (int i = 0; i < total; i++)
                {
                    pendingDelay += ReadDelayMs(image.Frames[i].Metadata, extension);
                    if (i % stride != 0) continue;

                    using var single = image.Frames.CloneFrame(i);
                    using var stream = new MemoryStream();
                    single.Save(stream, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
                    stream.Position = 0;

                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.DecodePixelWidth = width;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    animator.frames.Add(bitmap);
                    animator.delays.Add(pendingDelay);
                    pendingDelay = 0;
                }

                return animator.frames.Count > 1 ? animator : null;
            }
            catch (Exception ex)
            {
                LastError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        public void Attach(System.Windows.Controls.Image image)
        {
            Detach();
            if (frames.Count < 2) return;

            target = image;
            index = 0;
            image.Source = frames[0];

            timer ??= new DispatcherTimer();
            timer.Tick -= OnTick;
            timer.Tick += OnTick;
            timer.Interval = TimeSpan.FromMilliseconds(delays[0]);
            timer.Start();
        }

        public void Detach()
        {
            timer?.Stop();
            if (target != null && frames.Count > 0) target.Source = frames[0];
            target = null;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (target == null || frames.Count == 0) return;

            index = (index + 1) % frames.Count;
            target.Source = frames[index];
            if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(delays[index]);
        }
    }
}
