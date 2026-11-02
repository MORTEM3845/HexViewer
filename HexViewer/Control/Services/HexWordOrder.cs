using HexViewer.Core;
using HexViewer.Items.Enums;

namespace HexViewer.Control.Services;

internal static class HexWordOrder
{
    internal static int SourceIndex(int index, int count, int columns, int groupSize,
        int offset, HexByteDisplayOrder order) => HexWordLayout.SourceIndex(index, count, columns, groupSize, offset, order);

    internal static uint? Read(IList<byte> data, int start, int count, HexByteOrder order) =>
        HexWordLayout.Read(data, start, count, order);
}
