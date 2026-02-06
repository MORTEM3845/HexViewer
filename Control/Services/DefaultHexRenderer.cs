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
    public readonly struct RenderContext
    {
        public readonly int Columns;
        public readonly int GroupSize;
        public readonly double CellWidth;
        public readonly double CellHeight;
        public readonly double GroupSpacing;

        public readonly int FirstRow;
        public readonly int RowsToDraw;
        public readonly int OffsetDigits;

        public readonly int? HoveredGroupStartIndex;
        public readonly GeometryCache Geo;
        public readonly TextCache Texts;

        public readonly IHexDataSource? Data;

        public RenderContext(
            int columns, int groupSize, double cellWidth, double cellHeight, double groupSpacing,
            int firstRow, int rowsToDraw, int? hoveredGroupStartIndex,
            GeometryCache geo, TextCache texts, IHexDataSource? data, int offsetDigits)
        {
            Columns = columns;
            GroupSize = groupSize;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            GroupSpacing = groupSpacing;
            FirstRow = firstRow;
            RowsToDraw = rowsToDraw;
            OffsetDigits = offsetDigits;
            HoveredGroupStartIndex = hoveredGroupStartIndex;
            Geo = geo;
            Texts = texts;
            Data = data;
        }
    }

    public sealed class DefaultHexRenderer
    {
        private static readonly Pen GridPenLight = new(Brushes.LightGray, 1);
        private static readonly Pen GridPen = new(Brushes.Gray, 1);
        private static readonly SolidColorBrush HlBrush = new SolidColorBrush(Color.FromArgb(50, 0, 120, 215));
        static DefaultHexRenderer()
        {
            HlBrush.Freeze();
            GridPenLight.Freeze();
            GridPen.Freeze();
        }

        public void RenderHeader(DrawingContext dc, RenderContext ctx)
        {
            // Хедер — номера колонок в HEX
            for (int col = 0; col < ctx.Columns; col++)
            {
                double x = ctx.Geo.ColumnX[col];

                // формат в HEX, минимум 2 символа (00, 01, 0A, 0F и т.п.)
                var text = ctx.Texts.Create(col.ToString("X2"));

                dc.DrawText(
                    text,
                    new Point(x + (ctx.CellWidth - text.Width) / 2,
                              (ctx.CellHeight - text.Height) / 2)
                );
            }
        }

        public void RenderGridLines(DrawingContext dc, RenderContext ctx)
        {
            double contentWidth = ctx.Geo.OffsetColumnWidth + ctx.Columns * ctx.CellWidth + ((ctx.Columns - 1) / ctx.GroupSize) * ctx.GroupSpacing;

            // Вертикальная линия между колонкой offset и байтами
            dc.DrawLine(GridPen,new Point(ctx.Geo.OffsetColumnWidth, 0), new Point(ctx.Geo.OffsetColumnWidth, (ctx.RowsToDraw + 1) * ctx.CellHeight));

            // Горизонтальная линия под header
            dc.DrawLine(GridPen,new Point(0, ctx.CellHeight),new Point(contentWidth, ctx.CellHeight));
        }

        public void RenderRows(DrawingContext dc, RenderContext ctx)
        {
            for (int row = 0; row < ctx.RowsToDraw; row++)
            {
                int realRow = ctx.FirstRow + row;
                double y = ctx.CellHeight + row * ctx.CellHeight;
                int rowOffset = realRow * ctx.Columns;

                // Offset
                var offsetText = ctx.Texts.Create(rowOffset.ToString($"X{ctx.OffsetDigits}"));

                dc.DrawText(offsetText,new Point(ctx.Geo.OffsetColumnWidth - 8 - offsetText.Width, y + (ctx.CellHeight - offsetText.Height) / 2));

                // Подсветка — только если ховер попадает в текущую строку
                if (ctx.HoveredGroupStartIndex is int hovered && hovered / ctx.Columns == realRow)
                {
                    int colStart = (hovered % ctx.Columns) / ctx.GroupSize * ctx.GroupSize;
                    var rect = new Rect(ctx.Geo.ColumnX[colStart], y, ctx.CellWidth * ctx.GroupSize, ctx.CellHeight);

                    dc.DrawRectangle(HlBrush, null, rect);
                }

                // Bytes
                for (int col = 0; col < ctx.Columns; col++)
                {
                    int index = rowOffset + col;
                    double x = ctx.Geo.ColumnX[col];

                    var text = (ctx.Data != null && index < ctx.Data.Count) ? ctx.Texts.GetByte(ctx.Data[index]) : ctx.Texts.Empty;

                    dc.DrawText(text, new Point(x + (ctx.CellWidth - text.Width) / 2, y + (ctx.CellHeight - text.Height) / 2));
                }
            }
        }

        public void RenderShowMore(DrawingContext dc, RenderContext ctx, bool hovered, long bytesRemaining, out Rect showMoreRect)
        {
            string showMoreText = $"Показать всё ({FormatBytes(bytesRemaining)})";
            var text = ctx.Texts.Create(showMoreText);

            double contentWidth = ctx.Geo.OffsetColumnWidth +
                                  ctx.Columns * ctx.CellWidth +
                                  ((ctx.Columns - 1) / ctx.GroupSize) * ctx.GroupSpacing;

            double margin = 8;
            double x = contentWidth - text.Width - margin;
            double y = ctx.CellHeight + ctx.RowsToDraw * ctx.CellHeight + 8;

            showMoreRect = new Rect(x - 4, y - 2, text.Width + 8, text.Height + 4);

            // Текст
            dc.DrawText(text, new Point(x, y));

            // Подчёркивание при наведении
            if (hovered)
            {
                var pen = new Pen(Brushes.Black, 1);
                dc.DrawLine(pen, new Point(x, y + text.Height), new Point(x + text.Width, y + text.Height));
            }
        }

        private static string FormatBytes(long bytes)
        {
            return $"{bytes.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} Б";
        }
    }
}