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
    /// <summary>Farben für das dunkle Menü des Infobereich-Symbols.</summary>
    internal sealed class DarkMenuColors : Forms.ProfessionalColorTable
    {
        private static readonly System.Drawing.Color Surface = System.Drawing.Color.FromArgb(22, 27, 40);
        private static readonly System.Drawing.Color Border = System.Drawing.Color.FromArgb(42, 49, 66);
        private static readonly System.Drawing.Color Highlight = System.Drawing.Color.FromArgb(52, 60, 82);

        public override System.Drawing.Color ToolStripDropDownBackground => Surface;
        public override System.Drawing.Color ImageMarginGradientBegin => Surface;
        public override System.Drawing.Color ImageMarginGradientMiddle => Surface;
        public override System.Drawing.Color ImageMarginGradientEnd => Surface;
        public override System.Drawing.Color MenuBorder => Border;
        public override System.Drawing.Color MenuItemBorder => Highlight;
        public override System.Drawing.Color MenuItemSelected => Highlight;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Highlight;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Highlight;
        public override System.Drawing.Color SeparatorDark => Border;
        public override System.Drawing.Color SeparatorLight => Border;
    }

    internal sealed class DarkMenuRenderer : Forms.ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors()) { }
    }
}
