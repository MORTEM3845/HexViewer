using System.Windows;

namespace HexViewer.Control.Services
{
    public static class HitTestService
    {
        public static int GetRowIndexFromPoint(Point pt, double cellHeight)
        {
            if (pt.Y < cellHeight) return -1;
            return (int)((pt.Y - cellHeight) / cellHeight);
        }

        public static bool TryGetGroupStartIndex(Point pt, GeometryCache geo, int firstRow, int columns, int groupSize, double cellHeight, out int startIndex)
        {
            startIndex = -1;

            int row = GetRowIndexFromPoint(pt, cellHeight);
            if (row < 0) return false;

            // скрин-ряд -> реальный
            int realRow = firstRow + row;
            double y = cellHeight + row * cellHeight;

            // пройти по группам (по шаблону)
            for (int g = 0, col = 0; col < columns; g++, col += groupSize)
            {
                var r = geo.GroupRectsTemplate[g];
                var rect = new Rect(r.X, y, r.Width, r.Height);
                if (rect.Contains(pt))
                {
                    startIndex = realRow * columns + col;
                    return true;
                }
            }
            return false;
        }
    }
}
