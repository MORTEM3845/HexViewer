using System.Windows;
using System.Windows.Media;

namespace HexViewer.Control.Services
{
    public static class HexRenderResources
    {
        public static readonly Brush BackgroundBrush = Freeze(Brushes.White);
        public static readonly Brush TextBrush = Freeze(Brushes.Black);
        public static readonly Brush OffsetBrush = Freeze(Brushes.DimGray);
        public static readonly Brush ChangedBrush = Freeze(Brushes.DarkBlue);
        public static readonly Brush HeaderBrush = Freeze(Brushes.Gray);
        public static readonly Brush AsciiNonPrintableBrush = Freeze(Brushes.Gray);

        public static readonly Brush CurrentByteBrush = Freeze(new SolidColorBrush(Color.FromArgb(38, 30, 120, 220)));
        public static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromArgb(85, 30, 120, 220)));
        public static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(50, 0, 120, 215)));
        public static readonly Brush SearchHighlightBrush = Freeze(new SolidColorBrush(Color.FromArgb(150, 255, 214, 10)));

        public static readonly Pen GridPenLight = Freeze(new Pen(Brushes.LightGray, 1));
        public static readonly Pen GridPen = Freeze(new Pen(Brushes.Gainsboro, 1));
        public static readonly Pen GridPenDark = Freeze(new Pen(Brushes.Gray, 1));
        public static readonly Pen CaretPen = Freeze(new Pen(Brushes.Black, 1.4));

        private static T Freeze<T>(T value) where T : Freezable
        {
            if (value.CanFreeze)
                value.Freeze();

            return value;
        }
    }
}
