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
    /// <summary>Ordnet Karten in zwei oder drei Spalten Zeile für Zeile an. Alle Karten einer Zeile sind gleich hoch, damit Kanten und Abstände sauber untereinander und nebeneinander liegen.</summary>
    public sealed class SettingsGridPanel : System.Windows.Controls.Panel
    {
        public double MinColumnWidth { get; set; } = 620;
        public int MaxColumns { get; set; } = 3;
        private const double Gap = 18;

        private int ColumnsFor(double width)
            => Math.Max(1, Math.Min(MaxColumns, (int)Math.Floor((width + Gap) / (MinColumnWidth + Gap))));

        protected override System.Windows.Size MeasureOverride(System.Windows.Size available)
        {
            double width = double.IsInfinity(available.Width) ? 900 : available.Width;
            int columns = ColumnsFor(width);
            double columnWidth = (width - Gap * (columns - 1)) / columns;

            double total = 0;
            var row = new List<UIElement>();
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new System.Windows.Size(columnWidth, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed) continue;

                row.Add(child);
                if (row.Count == columns)
                {
                    total += row.Max(c => c.DesiredSize.Height);
                    row.Clear();
                }
            }
            if (row.Count > 0) total += row.Max(c => c.DesiredSize.Height);

            return new System.Windows.Size(width, total);
        }

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
        {
            int columns = ColumnsFor(finalSize.Width);
            double columnWidth = (finalSize.Width - Gap * (columns - 1)) / columns;

            double y = 0;
            var row = new List<UIElement>();

            void Place()
            {
                double height = row.Max(c => c.DesiredSize.Height);
                for (int i = 0; i < row.Count; i++)
                    row[i].Arrange(new System.Windows.Rect(i * (columnWidth + Gap), y, columnWidth, height));
                y += height;
                row.Clear();
            }

            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new System.Windows.Rect(0, 0, 0, 0));
                    continue;
                }

                row.Add(child);
                if (row.Count == columns) Place();
            }
            if (row.Count > 0) Place();

            return finalSize;
        }
    }
}
