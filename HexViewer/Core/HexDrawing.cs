using HexViewer.Control.Services;
using HexViewer.Items.Enums;

namespace HexViewer.Core;

public readonly record struct HexSize(double Width, double Height);
public readonly record struct HexRect(double X, double Y, double Width, double Height)
{
    public bool Contains(double x, double y) => x >= X && y >= Y && x <= X + Width && y <= Y + Height;
}

public enum HexInk { Text, Grid, Hover, Search }

/// <summary>Адаптер платформы измеряет и рисует текст; все координаты вычисляет C#-ядро.</summary>
public interface IHexCanvas
{
    HexSize Measure(string text);
    void Text(string text, double x, double y);
    void Fill(HexRect rect, HexInk ink);
    void Line(double x1, double y1, double x2, double y2, HexInk ink);
}

public sealed record HexViewport
{
    public HexDisplayMode DisplayMode { get; init; }
    public int Columns { get; init; } = 32;
    public int GroupSize { get; init; } = 4;
    public double CellWidth { get; init; } = 28;
    public double CellHeight { get; init; } = 20;
    public double GroupSpacing { get; init; } = 8;
    public double OffsetWidth { get; init; }
    public int OffsetDigits { get; init; } = 3;
    public int FirstRow { get; init; }
    public int Rows { get; init; } = 8;
    public int VisualByteOffset { get; init; }
    public int? HoveredGroup { get; init; }
    public int HighlightStart { get; init; } = -1;
    public int HighlightLength { get; init; }
    public bool IsCompact { get; init; }
    public bool ShowAscii { get; init; }
    public HexTextEncoding TextEncoding { get; init; }
    public HexByteDisplayOrder ByteDisplayOrder { get; init; }
    public double AsciiStartX { get; init; }
    public double AsciiCellWidth { get; init; } = 8;
    public double ColumnX(int column) => OffsetWidth + column * CellWidth + column / GroupSize * GroupSpacing;
    public double HexWidth => OffsetWidth + Columns * CellWidth + (Columns - 1) / GroupSize * GroupSpacing;
    public double Width => ShowAscii ? AsciiStartX + Columns * AsciiCellWidth : HexWidth;
    public double ContentTop => IsCompact || DisplayMode == HexDisplayMode.Word ? 0 : CellHeight;

    public bool TryGetGroup(double x, double y, int count, out int start, out int length)
    {
        start = -1; length = 0;
        if (DisplayMode == HexDisplayMode.Word) return false;
        if (!double.IsFinite(x) || !double.IsFinite(y) || y < ContentTop || y >= ContentTop + Rows * CellHeight) return false;
        int row = FirstRow + (int)((y - ContentTop) / CellHeight);
        for (int col = 0; col < Columns; col += GroupSize)
        {
            double left = ColumnX(col);
            if (x < left || x >= left + CellWidth * Math.Min(GroupSize, Columns - col)) continue;
            int raw = row * Columns + col - VisualByteOffset;
            start = Math.Max(0, raw);
            length = Math.Min(Math.Min(GroupSize, Columns - col) - Math.Max(0, -raw), count - start);
            return length > 0;
        }
        return false;
    }
}

public static class HexDrawing
{
    public static void Header(IHexCanvas canvas, HexViewport v)
    {
        if (v.IsCompact || v.DisplayMode == HexDisplayMode.Word) return;
        for (int col = 0; col < v.Columns; col++) Center(canvas, col.ToString("X2"), v.ColumnX(col), 0, v.CellWidth, v.CellHeight);
        if (v.ShowAscii) Vertical(canvas, "ASCII", v.AsciiStartX, 0, v.CellHeight);
    }

    public static void Grid(IHexCanvas canvas, HexViewport v)
    {
        if (v.IsCompact || v.DisplayMode == HexDisplayMode.Word) return;
        canvas.Line(v.OffsetWidth, 0, v.OffsetWidth, (v.Rows + 1) * v.CellHeight, HexInk.Grid);
        if (v.ShowAscii)
        {
            double x = (v.HexWidth + v.AsciiStartX) / 2;
            canvas.Line(x, 0, x, (v.Rows + 1) * v.CellHeight, HexInk.Grid);
        }
        canvas.Line(0, v.CellHeight, v.Width, v.CellHeight, HexInk.Grid);
    }

    public static void Rows(IHexCanvas canvas, HexViewport v, IHexDataSource? data)
    {
        for (int row = 0; row < v.Rows; row++)
        {
            int realRow = v.FirstRow + row;
            double y = v.ContentTop + row * v.CellHeight;
            int displayRowOffset = realRow * v.Columns;
            int rowOffset = Math.Max(0, displayRowOffset - v.VisualByteOffset);
            int textStart = Math.Max(0, v.VisualByteOffset - displayRowOffset);
            int textCount = data == null ? 0 : Math.Min(v.Columns - textStart, Math.Max(0, data.Count - rowOffset));
            if (v.DisplayMode == HexDisplayMode.Word)
            {
                if (data is not null)
                {
                    var text = HexWordLayout.Format(data, displayRowOffset - v.VisualByteOffset,
                        v.Columns, v.Columns, v.GroupSize, v.VisualByteOffset, v.ByteDisplayOrder);
                    Vertical(canvas, text, v.ColumnX(textStart), y, v.CellHeight);
                }
                continue;
            }
            var textCells = v.ShowAscii && data != null
                ? HexTextCodec.DecodeCells(data, rowOffset, textCount, v.TextEncoding) : Array.Empty<string>();
            if (!v.IsCompact)
            {
                string text = rowOffset.ToString($"X{v.OffsetDigits}");
                Vertical(canvas, text, v.OffsetWidth - 8 - canvas.Measure(text).Width, y, v.CellHeight);
            }
            if (v.HoveredGroup is int hovered && (hovered + v.VisualByteOffset) / v.Columns == realRow)
            {
                int colStart = ((hovered + v.VisualByteOffset) % v.Columns) / v.GroupSize * v.GroupSize;
                canvas.Fill(new(v.ColumnX(colStart), y,
                    v.CellWidth * Math.Min(v.GroupSize, v.Columns - colStart), v.CellHeight), HexInk.Hover);
            }
            for (int col = 0; col < v.Columns; col++)
            {
                int address = displayRowOffset + col - v.VisualByteOffset;
                int index = HexWordLayout.SourceIndex(address, data?.Count ?? 0, v.Columns, v.GroupSize, v.VisualByteOffset, v.ByteDisplayOrder);
                double x = v.ColumnX(col);
                if (Highlighted(index, v)) canvas.Fill(new(x, y, v.CellWidth, v.CellHeight), HexInk.Search);
                if (v.ShowAscii && Highlighted(address, v))
                    canvas.Fill(new(v.AsciiStartX + col * v.AsciiCellWidth, y, v.AsciiCellWidth, v.CellHeight), HexInk.Search);
                string text = data != null && index >= 0 && index < data.Count ? data[index].ToString("X2") : string.Empty;
                Center(canvas, text, x, y, v.CellWidth, v.CellHeight);
                if (v.ShowAscii && address >= 0 && data != null && address < data.Count)
                    Vertical(canvas, textCells[col - textStart], v.AsciiStartX + col * v.AsciiCellWidth, y, v.CellHeight);
            }
        }
    }

    public static HexRect ShowMore(IHexCanvas canvas, HexViewport v, bool hovered, long bytes)
    {
        string text = $"Показать всё ({bytes.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"))} Б)";
        var size = canvas.Measure(text);
        double x = v.Width - size.Width - 8, y = v.ContentTop + v.Rows * v.CellHeight + 8;
        canvas.Text(text, x, y);
        if (hovered) canvas.Line(x, y + size.Height, x + size.Width, y + size.Height, HexInk.Text);
        return new(x - 4, y - 2, size.Width + 8, size.Height + 4);
    }

    private static bool Highlighted(int index, HexViewport v) => v.HighlightStart >= 0 && index >= v.HighlightStart && index < (long)v.HighlightStart + v.HighlightLength;
    private static void Center(IHexCanvas c, string t, double x, double y, double w, double h)
    {
        var size = c.Measure(t); c.Text(t, x + (w - size.Width) / 2, y + (h - size.Height) / 2);
    }
    private static void Vertical(IHexCanvas c, string t, double x, double y, double h) => c.Text(t, x, y + (h - c.Measure(t).Height) / 2);
}
