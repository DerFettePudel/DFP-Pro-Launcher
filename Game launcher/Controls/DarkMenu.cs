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
    /// <summary>Farben für das dunkle Menü des Infobereich-Symbols (Markierung und Rahmen in der Akzentfarbe).</summary>
    internal sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(19, 23, 34);
        private readonly System.Drawing.Color Border;
        private readonly System.Drawing.Color Highlight;

        public DarkMenuColors(System.Drawing.Color accent)
        {
            // Akzentfarbe mit dem Untergrund mischen: 35 % für die Markierung, 45 % für den Rahmen
            System.Drawing.Color Mix(double amount) => System.Drawing.Color.FromArgb(
                (int)(Surface.R + (accent.R - Surface.R) * amount),
                (int)(Surface.G + (accent.G - Surface.G) * amount),
                (int)(Surface.B + (accent.B - Surface.B) * amount));
            Highlight = Mix(0.35);
            Border = Mix(0.45);
        }

        public override System.Drawing.Color ToolStripDropDownBackground => Surface;
        public override System.Drawing.Color ImageMarginGradientBegin => Surface;
        public override System.Drawing.Color ImageMarginGradientMiddle => Surface;
        public override System.Drawing.Color ImageMarginGradientEnd => Surface;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Highlight;
        public override System.Drawing.Color MenuItemSelected => Highlight;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Highlight;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Highlight;
        public override System.Drawing.Color SeparatorDark => System.Drawing.Color.FromArgb(42, 49, 66);
        public override System.Drawing.Color SeparatorLight => System.Drawing.Color.FromArgb(42, 49, 66);
        public override System.Drawing.Color CheckBackground => Highlight;
        public override System.Drawing.Color CheckSelectedBackground => Highlight;
        public override System.Drawing.Color CheckPressedBackground => Highlight;
    }

    internal sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : this(System.Drawing.Color.FromArgb(139, 92, 246)) { }

        public DarkMenuRenderer(System.Drawing.Color accent) : base(new DarkMenuColors(accent))
        {
            RoundedEdges = false;
        }
    }
}
