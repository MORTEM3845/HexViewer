using System.Globalization;
using System.Windows;
using System.Windows.Media;

using HexViewer.Items.Enums;

namespace HexViewer.Control.Services
{
    public readonly struct RenderContext
    {
        public readonly int Columns;
        public readonly HexByteDisplayOrder ByteDisplayOrder;
        public readonly HexDisplayMode DisplayMode;
        public readonly int GroupSize;
        public readonly double CellWidth;
        public readonly double CellHeight;
        public readonly double GroupSpacing;

        public readonly int FirstRow;
        public readonly int RowsToDraw;
        public readonly int OffsetDigits;
        public readonly int VisualByteOffset;
        public readonly int HighlightedRangeStart;
        public readonly int HighlightedRangeLength;
        public readonly bool IsCompact;
        public readonly bool ShowAscii;
        public readonly HexTextEncoding TextEncoding;
        public readonly double AsciiStartX;
        public readonly double AsciiCellWidth;

        public readonly int? HoveredGroupStartIndex;
        public readonly GeometryCache Geo;
        public readonly TextCache Texts;

        public readonly IHexDataSource? Data;

        public RenderContext(
            int columns, int groupSize, double cellWidth, double cellHeight, double groupSpacing,
            int firstRow, int rowsToDraw, int? hoveredGroupStartIndex,
            GeometryCache geo, TextCache texts, IHexDataSource? data, int offsetDigits, int visualByteOffset,
            int highlightedRangeStart, int highlightedRangeLength, bool isCompact,
            bool showAscii, HexTextEncoding textEncoding, double asciiStartX, double asciiCellWidth)
            : this(columns, groupSize, cellWidth, cellHeight, groupSpacing, firstRow, rowsToDraw,
                hoveredGroupStartIndex, geo, texts, data, offsetDigits, visualByteOffset,
                highlightedRangeStart, highlightedRangeLength, isCompact, showAscii, textEncoding,
                asciiStartX, asciiCellWidth, HexByteDisplayOrder.AddressOrder)
        {
        }

        public RenderContext(
            int columns, int groupSize, double cellWidth, double cellHeight, double groupSpacing,
            int firstRow, int rowsToDraw, int? hoveredGroupStartIndex,
            GeometryCache geo, TextCache texts, IHexDataSource? data, int offsetDigits, int visualByteOffset,
            int highlightedRangeStart, int highlightedRangeLength, bool isCompact,
            bool showAscii, HexTextEncoding textEncoding, double asciiStartX, double asciiCellWidth,
            HexByteDisplayOrder byteDisplayOrder, HexDisplayMode displayMode = HexDisplayMode.Bytes)
        {
            Columns = columns;
            ByteDisplayOrder = byteDisplayOrder;
            DisplayMode = displayMode;
            GroupSize = groupSize;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            GroupSpacing = groupSpacing;
            FirstRow = firstRow;
            RowsToDraw = rowsToDraw;
            OffsetDigits = offsetDigits;
            VisualByteOffset = visualByteOffset;
            HighlightedRangeStart = highlightedRangeStart;
            HighlightedRangeLength = highlightedRangeLength;
            IsCompact = isCompact;
            ShowAscii = showAscii;
            TextEncoding = textEncoding;
            AsciiStartX = asciiStartX;
            AsciiCellWidth = asciiCellWidth;
            HoveredGroupStartIndex = hoveredGroupStartIndex;
            Geo = geo;
            Texts = texts;
            Data = data;
        }

        public double HexContentWidth => Geo.OffsetColumnWidth + Columns * CellWidth +
                                         ((Columns - 1) / GroupSize) * GroupSpacing;

        public double ContentWidth => ShowAscii ? AsciiStartX + Columns * AsciiCellWidth : HexContentWidth;
    }

    public sealed class DefaultHexRenderer
    {
        private static global::HexViewer.Core.HexViewport View(RenderContext c) => new()
        {
            DisplayMode = c.DisplayMode,
            Columns = c.Columns, GroupSize = c.GroupSize, CellWidth = c.CellWidth, CellHeight = c.CellHeight,
            GroupSpacing = c.GroupSpacing, OffsetWidth = c.Geo.OffsetColumnWidth, OffsetDigits = c.OffsetDigits,
            FirstRow = c.FirstRow, Rows = c.RowsToDraw, VisualByteOffset = c.VisualByteOffset,
            HoveredGroup = c.HoveredGroupStartIndex, HighlightStart = c.HighlightedRangeStart,
            HighlightLength = c.HighlightedRangeLength, IsCompact = c.IsCompact, ShowAscii = c.ShowAscii,
            TextEncoding = c.TextEncoding, ByteDisplayOrder = c.ByteDisplayOrder,
            AsciiStartX = c.AsciiStartX, AsciiCellWidth = c.AsciiCellWidth
        };

        public void RenderHeader(DrawingContext dc, RenderContext ctx) =>
            global::HexViewer.Core.HexDrawing.Header(new Canvas(dc, ctx.Texts), View(ctx));
        public void RenderGridLines(DrawingContext dc, RenderContext ctx) =>
            global::HexViewer.Core.HexDrawing.Grid(new Canvas(dc, ctx.Texts), View(ctx));
        public void RenderRows(DrawingContext dc, RenderContext ctx) =>
            global::HexViewer.Core.HexDrawing.Rows(new Canvas(dc, ctx.Texts), View(ctx), ctx.Data);
        public void RenderShowMore(DrawingContext dc, RenderContext ctx, bool hovered, long bytesRemaining, out Rect showMoreRect)
        {
            var rect = global::HexViewer.Core.HexDrawing.ShowMore(new Canvas(dc, ctx.Texts), View(ctx), hovered, bytesRemaining);
            showMoreRect = new Rect(rect.X, rect.Y, rect.Width, rect.Height);
        }

        private sealed class Canvas(DrawingContext dc, TextCache texts) : global::HexViewer.Core.IHexCanvas
        {
            private readonly Dictionary<string, FormattedText> _cache = new();
            private FormattedText TextValue(string text)
            {
                if (text.Length == 0) return texts.Empty;
                if (text.Length == 2 && byte.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                    return texts.GetByte(value);
                if (!_cache.TryGetValue(text, out var result)) _cache[text] = result = texts.Create(text);
                return result;
            }
            public global::HexViewer.Core.HexSize Measure(string text)
            {
                var value = TextValue(text); return new(value.Width, value.Height);
            }
            public void Text(string text, double x, double y) => dc.DrawText(TextValue(text), new Point(x, y));
            private static Brush Brush(global::HexViewer.Core.HexInk ink) => ink switch
            {
                global::HexViewer.Core.HexInk.Hover => HexRenderResources.HoverBrush,
                global::HexViewer.Core.HexInk.Search => HexRenderResources.SearchHighlightBrush,
                _ => Brushes.Black
            };
            public void Fill(global::HexViewer.Core.HexRect rect, global::HexViewer.Core.HexInk ink) =>
                dc.DrawRectangle(Brush(ink), null, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
            public void Line(double x1, double y1, double x2, double y2, global::HexViewer.Core.HexInk ink) =>
                dc.DrawLine(ink == global::HexViewer.Core.HexInk.Grid ? HexRenderResources.GridPen : new Pen(Brush(ink), 1), new Point(x1, y1), new Point(x2, y2));
        }
    }
}
