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
    /// <summary>Spielt animierte GIFs, APNGs und WebPs ab (WPF kann das nicht von Haus aus). Gelesen wird über <see cref="AnimatedImageDecoder"/>.</summary>
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

                // höchstens 150 Bilder lesen und etwa 90 davon im Speicher halten
                var decoded = AnimatedImageDecoder.Decode(path, maxWidth, 150, 90, out int total);
                if (total < 2 || decoded.Count < 2)
                {
                    LastError = "Das Bild enthält keine Animation";
                    return null;
                }

                var animator = new GifAnimator();
                foreach (var (frame, delay) in decoded)
                {
                    animator.frames.Add(frame);
                    animator.delays.Add(delay);
                }

                return animator;
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
