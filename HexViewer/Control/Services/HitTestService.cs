using System.Windows;

namespace HexViewer.Control.Services
{
    public static class HitTestService
    {
        public static int GetRowIndexFromPoint(Point pt, double cellHeight)
        {
            return GetRowIndexFromPoint(pt, cellHeight, false);
        }

        public static int GetRowIndexFromPoint(Point pt, double cellHeight, bool isCompact)
        {
            double contentTop = isCompact ? 0 : cellHeight;
            if (pt.Y < contentTop) return -1;
            return (int)((pt.Y - contentTop) / cellHeight);
        }

        public static bool TryGetGroup(
            Point pt,
            GeometryCache geo,
            int firstRow,
            int visibleRows,
            int columns,
            int groupSize,
            double cellHeight,
            int visualByteOffset,
            int dataCount,
            out int startIndex,
            out int byteCount)
        {
            return TryGetGroup(pt, geo, firstRow, visibleRows, columns, groupSize, cellHeight,
                visualByteOffset, dataCount, false, out startIndex, out byteCount);
        }

        public static bool TryGetGroup(
            Point pt,
            GeometryCache geo,
            int firstRow,
            int visibleRows,
            int columns,
            int groupSize,
            double cellHeight,
            int visualByteOffset,
            int dataCount,
            bool isCompact,
            out int startIndex,
            out int byteCount)
        {
            startIndex = -1;
            byteCount = 0;

            int row = GetRowIndexFromPoint(pt, cellHeight, isCompact);
            if (row < 0 || row >= visibleRows || columns <= 0 || groupSize <= 0 || cellHeight <= 0) return false;

            // скрин-ряд -> реальный
            int realRow = firstRow + row;
            double y = (isCompact ? 0 : cellHeight) + row * cellHeight;

            // пройти по группам (по шаблону)
            for (int g = 0, col = 0; col < columns && g < geo.GroupRectsTemplate.Length; g++, col += groupSize)
            {
                var r = geo.GroupRectsTemplate[g];
                var rect = new Rect(r.X, y, r.Width, r.Height);
                if (rect.Contains(pt))
                {
                    int displayStartIndex = realRow * columns + col;
                    int dataStartIndex = displayStartIndex - visualByteOffset;
                    int leadingEmptyCells = Math.Max(0, -dataStartIndex);

                    startIndex = Math.Max(0, dataStartIndex);
                    int cellsInRow = Math.Min(groupSize, columns - col);
                    byteCount = Math.Min(cellsInRow - leadingEmptyCells, dataCount - startIndex);
                    return byteCount > 0;
                }
            }
            return false;
        }
    }
}
