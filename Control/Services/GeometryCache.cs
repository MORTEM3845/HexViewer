using System.Windows;

namespace HexViewer.Control.Services
{
    public sealed class GeometryCache
    {
        public double[] ColumnX { get; private set; } = Array.Empty<double>();
        public Rect[] GroupRectsTemplate { get; private set; } = Array.Empty<Rect>();
        public double OffsetColumnWidth { get; private set; }

        // Кэш последних параметров
        private int _lastColumns;
        private int _lastGroupSize;
        private double _lastGroupSpacing;
        private double _lastCellWidth;
        private double _lastCellHeight;
        private double _lastOffsetWidth;
        private bool _initialized;

        public void Rebuild(int columns, int groupSize, double groupSpacing,
                            double cellWidth, double cellHeight, double offsetWidth)
        {
            // Проверка — если параметры те же, пересборка не нужна
            if (_initialized &&
                _lastColumns == columns &&
                _lastGroupSize == groupSize &&
                Math.Abs(_lastGroupSpacing - groupSpacing) < double.Epsilon &&
                Math.Abs(_lastCellWidth - cellWidth) < double.Epsilon &&
                Math.Abs(_lastCellHeight - cellHeight) < double.Epsilon &&
                Math.Abs(_lastOffsetWidth - offsetWidth) < double.Epsilon)
            {
                return;
            }

            _initialized = true;

            _lastColumns = columns;
            _lastGroupSize = groupSize;
            _lastGroupSpacing = groupSpacing;
            _lastCellWidth = cellWidth;
            _lastCellHeight = cellHeight;
            _lastOffsetWidth = offsetWidth;

            OffsetColumnWidth = offsetWidth;

            ColumnX = new double[columns];
            for (int col = 0; col < columns; col++)
            {
                int groupsBefore = col / groupSize;
                ColumnX[col] = offsetWidth + col * cellWidth + groupsBefore * groupSpacing;
            }

            int groups = (columns - 1) / groupSize + 1;
            GroupRectsTemplate = new Rect[groups];
            for (int g = 0, c = 0; g < groups; g++, c += groupSize)
            {
                GroupRectsTemplate[g] = new Rect(ColumnX[c], 0, cellWidth * groupSize, cellHeight);
            }
        }
    }
}
