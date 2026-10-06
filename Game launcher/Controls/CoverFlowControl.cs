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
    /// <summary>Blättert Cover flüssig von links nach rechts durch (Cover-Flow). Es werden nur die Einträge um die Auswahl herum gebaut.</summary>
    public sealed class CoverFlowControl : Canvas
    {
        private readonly Dictionary<int, FrameworkElement> items = new();
        private double position;
        private double target;
        private bool rendering;
        private TimeSpan lastFrame = TimeSpan.Zero;

        public int Count { get; private set; }
        public double ItemWidth { get; set; } = 210;
        public double ItemHeight { get; set; } = 315;
        public Func<int, FrameworkElement>? ItemFactory { get; set; }
        public Action<FrameworkElement, double>? FocusChanged { get; set; }
        public event EventHandler? SelectionChanged;
        public event EventHandler<int>? ItemActivated;

        public int SelectedIndex => Count == 0 ? -1 : Math.Clamp((int)Math.Round(target), 0, Count - 1);

        public CoverFlowControl()
        {
            Background = System.Windows.Media.Brushes.Transparent;
            SizeChanged += (s, e) => ArrangeItems();
            Unloaded += (s, e) => StopRendering();
        }

        public void SetCount(int count, int selected)
        {
            foreach (var element in items.Values) Children.Remove(element);
            items.Clear();

            Count = Math.Max(0, count);
            target = position = Count == 0 ? 0 : Math.Clamp(selected, 0, Count - 1);
            ArrangeItems();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Move(int delta) => Select(SelectedIndex + delta);

        public void Select(int index)
        {
            if (Count == 0) return;

            index = Math.Clamp(index, 0, Count - 1);
            if (Math.Abs(target - index) < 0.001) return;

            target = index;
            StartRendering();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            Move(e.Delta > 0 ? -1 : 1);
            e.Handled = true;
        }

        private void StartRendering()
        {
            if (rendering) return;
            rendering = true;
            lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }

        private void StopRendering()
        {
            if (!rendering) return;
            rendering = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        private void OnFrame(object? sender, EventArgs e)
        {
            TimeSpan now = (e as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
            if (now == lastFrame && now != TimeSpan.Zero) return;

            double dt = lastFrame == TimeSpan.Zero ? 0.016 : Math.Clamp((now - lastFrame).TotalSeconds, 0.001, 0.05);
            lastFrame = now;

            double difference = target - position;
            if (Math.Abs(difference) < 0.003)
            {
                position = target;
                ArrangeItems();
                StopRendering();
                return;
            }

            position += difference * (1 - Math.Exp(-dt * 12));
            ArrangeItems();
        }

        private void ArrangeItems()
        {
            if (ItemFactory == null || Count == 0 || ActualWidth < 20 || ActualHeight < 20) return;

            double centerX = ActualWidth / 2, centerY = ActualHeight / 2;
            int first = Math.Max(0, (int)Math.Floor(position) - 5);
            int last = Math.Min(Count - 1, (int)Math.Ceiling(position) + 5);

            foreach (int key in items.Keys.Where(k => k < first || k > last).ToList())
            {
                Children.Remove(items[key]);
                items.Remove(key);
            }

            for (int i = first; i <= last; i++)
            {
                if (!items.TryGetValue(i, out var element))
                {
                    element = ItemFactory(i);
                    element.Width = ItemWidth;
                    element.Height = ItemHeight;
                    element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                    element.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform(0, 0) } };
                    element.CacheMode = new BitmapCache();

                    int index = i;
                    element.MouseLeftButtonUp += (s, e) =>
                    {
                        if (index == SelectedIndex) ItemActivated?.Invoke(this, index);
                        else Select(index);
                    };

                    items[i] = element;
                    Children.Add(element);
                }

                double d = i - position;
                double ad = Math.Abs(d);
                double scale = 1.0 / (1.0 + 0.34 * Math.Min(ad, 3.0));
                double slide = Math.Sign(d) * ItemWidth * (0.80 * Math.Min(ad, 1.0) + 0.42 * Math.Max(0.0, Math.Min(ad, 5.0) - 1.0));

                SetLeft(element, centerX - ItemWidth / 2);
                SetTop(element, centerY - ItemHeight / 2);

                var group = (TransformGroup)element.RenderTransform;
                var scaleTransform = (ScaleTransform)group.Children[0];
                var moveTransform = (TranslateTransform)group.Children[1];
                scaleTransform.ScaleX = scale;
                scaleTransform.ScaleY = scale;
                moveTransform.X = slide;

                element.Opacity = Math.Max(0.0, 1.0 - 0.17 * Math.Min(ad, 5.0));
                SetZIndex(element, 100 - (int)Math.Round(ad * 10));
                FocusChanged?.Invoke(element, Math.Max(0.0, 1.0 - ad));
            }
        }
    }
}
