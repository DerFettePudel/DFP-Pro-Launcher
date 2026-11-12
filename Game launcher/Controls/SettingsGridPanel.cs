using System;
using System.Linq;
using System.Windows;

namespace Game_launcher
{
    /// <summary>
    /// Ordnet Karten in zwei oder drei Spalten an (Spaltenlayout): Jede Karte ist nur so hoch wie ihr Inhalt und kommt
    /// in die Spalte, die gerade am kürzesten ist. So gibt es keine riesigen Kacheln für eine einzige Option mehr.
    /// Die Reihenfolge bleibt erhalten (von links nach rechts, dann nach unten).
    /// </summary>
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

            var heights = new double[columns];
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new System.Windows.Size(columnWidth, double.PositiveInfinity));
                if (child.Visibility == Visibility.Collapsed) continue;

                int column = Shortest(heights);
                heights[column] += child.DesiredSize.Height;
            }

            return new System.Windows.Size(width, heights.Max());
        }

        protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
        {
            int columns = ColumnsFor(finalSize.Width);
            double columnWidth = (finalSize.Width - Gap * (columns - 1)) / columns;

            var heights = new double[columns];
            foreach (UIElement child in InternalChildren)
            {
                if (child.Visibility == Visibility.Collapsed)
                {
                    child.Arrange(new System.Windows.Rect(0, 0, 0, 0));
                    continue;
                }

                int column = Shortest(heights);
                double height = child.DesiredSize.Height;
                child.Arrange(new System.Windows.Rect(column * (columnWidth + Gap), heights[column], columnWidth, height));
                heights[column] += height;
            }

            return finalSize;
        }

        private static int Shortest(double[] heights)
        {
            int best = 0;
            for (int i = 1; i < heights.Length; i++)
                if (heights[i] < heights[best] - 0.5) best = i;
            return best;
        }
    }
}
