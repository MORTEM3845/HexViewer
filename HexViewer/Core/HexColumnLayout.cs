namespace HexViewer.Core;

/// <summary>Общий подбор колонок для просмотра и редактирования WPF.</summary>
public static class HexColumnLayout
{
    public static int FitColumns(double availableWidth, int fallbackColumns, double cellWidth,
        int groupSize, double groupSpacing, double reservedWidth, double asciiCellWidth = 0)
    {
        if (!double.IsFinite(availableWidth)) return Math.Max(4, fallbackColumns / 4 * 4);
        var width = Math.Max(0, availableWidth - reservedWidth);
        int low = 1, high = (int)Math.Clamp(Math.Floor(width / (cellWidth + asciiCellWidth)), 1, int.MaxValue / 2);
        while (low < high)
        {
            int candidate = low + (high - low + 1) / 2;
            double required = candidate * (cellWidth + asciiCellWidth)
                + (groupSize > 0 ? (candidate - 1) / groupSize * groupSpacing : 0);
            if (required <= width) low = candidate;
            else high = candidate - 1;
        }
        return Math.Max(4, low / 4 * 4);
    }
}
