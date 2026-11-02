using HexViewer.Items.Enums;
using HexViewer.Control.Services;

namespace HexViewer.Core;

public static class HexWordLayout
{
    internal static string Format(IHexDataSource data, int start, int count, int columns, int groupSize,
        int offset, HexByteDisplayOrder order, int selectionStart = 0, int selectionLength = int.MaxValue)
    {
        var text = new System.Text.StringBuilder();
        for (var index = start; index < start + count; index++)
        {
            var source = SourceIndex(index, data.Count, columns, groupSize, offset, order);
            if (source >= selectionStart && source < data.Count && source < (long)selectionStart + selectionLength)
                text.Append(data[source].ToString("X2"));
        }
        return text.ToString();
    }

    public static int SourceIndex(int index, int count, int columns, int groupSize,
        int offset, HexByteDisplayOrder order)
    {
        if (order == HexByteDisplayOrder.AddressOrder || index < 0 || index >= count) return index;
        int display = index + offset;
        int rowStart = display / columns * columns;
        int groupColumn = (display % columns) / groupSize * groupSize;
        int start = Math.Max(0, rowStart + groupColumn - offset);
        int end = (int)Math.Min(count, (long)rowStart + Math.Min(columns, groupColumn + groupSize) - offset);
        return start + end - 1 - index;
    }

    public static uint? Read(IList<byte> data, int start, int count, HexByteOrder order)
    {
        count = Math.Min(count, data.Count - start);
        if (start < 0 || count <= 0 || count > 4) return null;
        uint value = 0;
        for (int i = 0; i < count; i++)
            value |= (uint)data[start + i] << ((order == HexByteOrder.LittleEndian ? i : count - 1 - i) * 8);
        return value;
    }
}
