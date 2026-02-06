using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace HexViewer.Control.Services
{
    public sealed class TextCache
    {
        private readonly FormattedText[] _bytes = new FormattedText[256];
        public FormattedText Empty { get; private set; } = null!;
        public FormattedText WidestOffset { get; private set; } = null!;

        private Typeface _tf = new Typeface("Consolas");
        private double _fs = 15;
        private Brush _brush = Brushes.Black;
        private double _ppd = 1.0;

        public void Rebuild(Typeface tf, double fontSize, Brush brush, double pixelsPerDip, int offsetDigits)
        {
            _tf = tf; _fs = fontSize; _brush = brush; _ppd = pixelsPerDip;

            for (int i = 0; i < 256; i++)
                _bytes[i] = Create($"{i:X2}");

            Empty = Create(string.Empty);
            WidestOffset = Create(new string('9', offsetDigits));
        }

        public FormattedText GetByte(int b) => _bytes[b];

        public FormattedText Create(string s) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _tf, _fs, _brush, _ppd);
    }
}
