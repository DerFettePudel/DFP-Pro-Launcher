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
    /// <summary>Legt einen dunklen, gemusterten Balken über ein Element, damit private Angaben im Stream nicht zu sehen sind.</summary>
    public sealed class CensorAdorner : System.Windows.Documents.Adorner
    {
        private static readonly System.Windows.Media.Brush BarFill;
        private static readonly System.Windows.Media.Pen BarFrame;
        private static readonly System.Windows.Media.Pen BarShine;
        private static readonly System.Windows.Media.Brush BarHatch;
        private static readonly System.Windows.Media.Brush GlyphBrush;
        private readonly System.Windows.Media.Brush accent;

        static CensorAdorner()
        {
            var fill = new System.Windows.Media.LinearGradientBrush(
                System.Windows.Media.Color.FromRgb(0x1C, 0x1F, 0x2B),
                System.Windows.Media.Color.FromRgb(0x0A, 0x0B, 0x10),
                90);
            fill.Freeze();
            BarFill = fill;

            var frame = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x35, 0x48)), 1);
            frame.Freeze();
            BarFrame = frame;

            var shine = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), 1);
            shine.Freeze();
            BarShine = shine;

            var stripe = new System.Windows.Media.Pen(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)), 1);
            stripe.Freeze();
            var drawing = new System.Windows.Media.GeometryDrawing(null, stripe,
                new System.Windows.Media.LineGeometry(new System.Windows.Point(0, 8), new System.Windows.Point(8, 0)));
            var hatch = new System.Windows.Media.DrawingBrush(drawing)
            {
                TileMode = System.Windows.Media.TileMode.Tile,
                Viewport = new System.Windows.Rect(0, 0, 8, 8),
                ViewportUnits = System.Windows.Media.BrushMappingMode.Absolute,
                Viewbox = new System.Windows.Rect(0, 0, 8, 8),
                ViewboxUnits = System.Windows.Media.BrushMappingMode.Absolute
            };
            hatch.Freeze();
            BarHatch = hatch;

            var glyph = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA3, 0xB8));
            glyph.Freeze();
            GlyphBrush = glyph;
        }

        public CensorAdorner(FrameworkElement element, System.Windows.Media.Brush? accentBrush) : base(element)
        {
            accent = accentBrush ?? System.Windows.Media.Brushes.MediumPurple;
            IsHitTestVisible = false;
            Visibility = element.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            element.IsVisibleChanged += (s, e) =>
            {
                Visibility = element.IsVisible ? Visibility.Visible : Visibility.Collapsed;
                InvalidateVisual();
            };
            element.SizeChanged += (s, e) => InvalidateVisual();
        }

        private double MeasureText(TextBlock block)
        {
            try
            {
                var text = new System.Windows.Media.FormattedText(
                    block.Text,
                    CultureInfo.CurrentUICulture,
                    block.FlowDirection,
                    new System.Windows.Media.Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
                    block.FontSize,
                    System.Windows.Media.Brushes.Black,
                    System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);
                return text.WidthIncludingTrailingWhitespace;
            }
            catch
            {
                return 0;
            }
        }

        private System.Windows.Media.FormattedText Glyph(string text, string font, double size, System.Windows.Media.Brush brush)
            => new(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(font), size, brush, System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            if (!AdornedElement.IsVisible) return;

            double width = AdornedElement.RenderSize.Width, height = AdornedElement.RenderSize.Height;
            if (width < 4 || height < 4) return;

            bool panel = height >= 56 && width >= 170;
            double barWidth = width, offset = 0;

            if (!panel && AdornedElement is TextBlock block && block.TextWrapping == TextWrapping.NoWrap && block.Text.Length > 0)
            {
                double textWidth = MeasureText(block);
                if (textWidth > 0 && textWidth < width)
                {
                    barWidth = textWidth;
                    offset = block.TextAlignment == TextAlignment.Right ? width - textWidth
                           : block.TextAlignment == TextAlignment.Center ? (width - textWidth) / 2
                           : 0;
                }
            }

            double pad = panel ? 0 : 3;
            var rect = new System.Windows.Rect(offset - pad, panel ? 0 : -1, barWidth + pad * 2, height + (panel ? 0 : 2));
            double radius = panel ? 14 : Math.Min(7, rect.Height / 2.2);

            dc.DrawRoundedRectangle(BarFill, BarFrame, rect, radius, radius);
            dc.DrawRoundedRectangle(BarHatch, null, rect, radius, radius);
            dc.DrawLine(BarShine, new System.Windows.Point(rect.Left + radius, rect.Top + 1.5), new System.Windows.Point(rect.Right - radius, rect.Top + 1.5));
            dc.DrawRoundedRectangle(accent, null, new System.Windows.Rect(rect.Left + 4, rect.Top + rect.Height * 0.22, 3, rect.Height * 0.56), 1.5, 1.5);

            if (panel)
            {
                var lockGlyph = Glyph("🔒", "Segoe UI Emoji", 28, GlyphBrush);
                var label = Glyph(Loc.T("Im Streamer-Modus verborgen"), "Segoe UI", 13, GlyphBrush);
                double top = rect.Top + (rect.Height - lockGlyph.Height - label.Height - 6) / 2;
                dc.DrawText(lockGlyph, new System.Windows.Point(rect.Left + (rect.Width - lockGlyph.Width) / 2, top));
                dc.DrawText(label, new System.Windows.Point(rect.Left + (rect.Width - label.Width) / 2, top + lockGlyph.Height + 6));
            }
            else if (rect.Width >= 34 && rect.Height >= 12)
            {
                var lockGlyph = Glyph("🔒", "Segoe UI Emoji", Math.Clamp(rect.Height * 0.55, 9, 15), GlyphBrush);
                dc.DrawText(lockGlyph, new System.Windows.Point(rect.Left + (rect.Width - lockGlyph.Width) / 2 + 3, rect.Top + (rect.Height - lockGlyph.Height) / 2));
            }
        }
    }
}
