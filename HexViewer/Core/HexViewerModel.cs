using HexViewer.Control.Services;
using HexViewer.Items;
using HexViewer.Items.Enums;

namespace HexViewer.Core;

/// <summary>Параметры просмотра. Не зависят от системы окон и не преобразуют пакет.</summary>
public sealed record HexViewerOptions
{
    public HexDisplayMode DisplayMode { get; init; }
    public int Columns { get; init; } = 32;
    public bool AutoFitColumnsToWidth { get; init; }
    public int GroupSize { get; init; } = 4;
    public double CellWidth { get; init; } = 28;
    public double CellHeight { get; init; } = 20;
    public double GroupSpacing { get; init; } = 8;
    public int VisibleRows { get; init; } = 8;
    public bool AutoFitRowsToHeight { get; init; }
    public bool ShowMoreButtonEnabled { get; init; } = true;
    public bool IsCompact { get; init; }
    public bool ShowAscii { get; init; }
    public bool IsEditable { get; init; } = true;
    public bool IsLoadFromBinVisible { get; init; } = true;
    public int VisualByteOffset { get; init; }
    public int HighlightedRangeStart { get; init; } = -1;
    public int HighlightedRangeLength { get; init; }
    public string FontFamily { get; init; } = "Consolas";
    public double FontSize { get; init; } = 15;
    public HexByteOrder ByteOrder { get; init; }
    public HexByteDisplayOrder ByteDisplayOrder { get; init; }
    public HexTextEncoding TextEncoding { get; init; }
    public WordSizeEnum WordDisplaySize { get; init; } = WordSizeEnum.Byte4;
}

/// <summary>Состояние и поведение просмотра для адаптеров UI. Владелец вызывает Refresh после изменения IList.</summary>
public sealed class HexViewerModel
{
    private IList<byte> _data = Array.Empty<byte>();
    private HexViewerOptions _options = new();
    private int _firstRow;
    private int? _hovered;
    private int _hoverLength;
    private HexRect _showMore;
    private bool _showMoreVisible;
    private bool _showMoreHovered;
    private double _availableHeight = double.PositiveInfinity;
    private int _fittedColumns = 32;
    public int DisplayColumns => Options.AutoFitColumnsToWidth ? _fittedColumns : Options.Columns;
    public event EventHandler? Changed;
    public event EventHandler? DataChangedExternally;
    public event EventHandler? OpenViewerRequested;
    public HexViewport? Viewport { get; private set; }
    public IList<byte> Data
    {
        get => _data;
        set { _data = value ?? Array.Empty<byte>(); _firstRow = 0; Refresh(); }
    }
    public HexViewerOptions Options
    {
        get => _options;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (!Enum.IsDefined(value.DisplayMode) || value.Columns < 1 || value.GroupSize < 1 || value.VisibleRows < 1 || value.VisualByteOffset is < 0 or > 3 ||
                !Positive(value.CellWidth) || !Positive(value.CellHeight) || !Positive(value.FontSize) ||
                !double.IsFinite(value.GroupSpacing) || value.GroupSpacing < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Некорректная геометрия просмотра.");
            _options = value; Refresh();
        }
    }
    public int TotalRows => Data.Count == 0 ? 0 : (int)(((long)Data.Count + Options.VisualByteOffset + DisplayColumns - 1) / DisplayColumns);
    public int FirstVisibleRowIndex
    {
        get => _firstRow;
        set
        {
            int next = Math.Clamp(value, 0, Math.Max(0, TotalRows - CalculateRows(_availableHeight)));
            if (next == _firstRow) return;
            _firstRow = next; _hovered = null; _hoverLength = 0;
            if (Viewport != null) Viewport = Viewport with { FirstRow = next, HoveredGroup = null };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
    public uint? HoveredValue => _hovered is int start ? HexWordLayout.Read(Data, start, _hoverLength, Options.ByteOrder) : null;

    public void Refresh()
    {
        _hovered = null; _hoverLength = 0;
        _showMoreHovered = false;
        _showMoreVisible = false;
        Viewport = null;
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, TotalRows - CalculateRows(_availableHeight)));
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private int CalculateRows(double availableHeight)
    {
        var o = Options;
        int rows = Math.Max(1, Math.Min(o.VisibleRows, TotalRows));
        if (o.AutoFitRowsToHeight && double.IsFinite(availableHeight))
        {
            var compact = o.IsCompact || o.DisplayMode == HexDisplayMode.Word;
            int without = Math.Max(1, (int)Math.Floor(availableHeight / o.CellHeight) - (compact ? 0 : 1));
            rows = Math.Max(1, Math.Min(TotalRows, o.ShowMoreButtonEnabled && without < TotalRows
                ? Math.Max(1, (int)Math.Floor((availableHeight - 10) / o.CellHeight) - (compact ? 0 : 1) - 1) : without));
        }
        return rows;
    }
    public void Render(IHexCanvas canvas, double availableHeight, double availableWidth = double.PositiveInfinity)
    {
        _availableHeight = availableHeight;
        var o = Options;
        bool word = o.DisplayMode == HexDisplayMode.Word;
        bool compact = o.IsCompact || word, ascii = o.ShowAscii && !word;
        double cellWidth = word ? canvas.Measure("00").Width : o.CellWidth * o.FontSize / 15;
        double spacing = word ? 0 : o.GroupSpacing * o.FontSize / 15;
        double asciiWidth = Math.Max(8 * o.FontSize / 15, canvas.Measure("W").Width);
        int fitDigits = Math.Max(3, Math.Max(0, Data.Count - 1).ToString("X").Length);
        double fitOffset = compact ? 0 : canvas.Measure(new string('F', fitDigits)).Width + 8;
        int columns = o.AutoFitColumnsToWidth ? HexColumnLayout.FitColumns(availableWidth, o.Columns,
            cellWidth, o.GroupSize, spacing, fitOffset + (ascii ? 22 : 0), ascii ? asciiWidth : 0) : o.Columns;
        if (_fittedColumns != columns) { _hovered = null; _hoverLength = 0; }
        _fittedColumns = columns;
        int rows = CalculateRows(availableHeight);
        _firstRow = Math.Clamp(_firstRow, 0, Math.Max(0, TotalRows - rows));
        int digits = Math.Max(3, Math.Max(0, (TotalRows - 1) * columns - o.VisualByteOffset).ToString("X").Length);
        double offset = compact ? 0 : canvas.Measure(new string('F', digits)).Width + 8;
        double hexWidth = offset + columns * cellWidth + (columns - 1) / o.GroupSize * spacing;
        var v = new HexViewport
        {
            DisplayMode = o.DisplayMode,
            Columns = columns, GroupSize = o.GroupSize, CellWidth = cellWidth, CellHeight = o.CellHeight,
            GroupSpacing = spacing, OffsetWidth = offset, OffsetDigits = digits, FirstRow = _firstRow,
            Rows = rows, VisualByteOffset = o.VisualByteOffset, HoveredGroup = _hovered,
            HighlightStart = o.HighlightedRangeStart, HighlightLength = o.HighlightedRangeLength,
            IsCompact = compact, ShowAscii = ascii, TextEncoding = o.TextEncoding,
            ByteDisplayOrder = o.ByteDisplayOrder, AsciiStartX = hexWidth + 22, AsciiCellWidth = asciiWidth
        };
        Viewport = v;
        HexDrawing.Header(canvas, v); HexDrawing.Grid(canvas, v); HexDrawing.Rows(canvas, v, new ListHexDataSource(Data));
        _showMoreVisible = o.ShowMoreButtonEnabled && rows < TotalRows;
        if (_showMoreVisible) _showMore = HexDrawing.ShowMore(canvas, v, _showMoreHovered, Data.Count);
    }
    public string Hover(double x, double y)
    {
        int? previous = _hovered;
        bool previousLink = _showMoreHovered;
        _showMoreHovered = _showMoreVisible && _showMore.Contains(x, y);
        int start = -1, length = 0;
        _hovered = Viewport?.TryGetGroup(x, y, Data.Count, out start, out length) == true ? start : null;
        _hoverLength = _hovered.HasValue ? length : 0;
        if (previous != _hovered || previousLink != _showMoreHovered) Changed?.Invoke(this, EventArgs.Empty);
        if (HoveredValue is not uint value || _hovered is not int address) return "";
        return HexTooltip.Format(value, Data, address, _hoverLength, Options.WordDisplaySize);
    }
    public void Click(double x, double y) { if (_showMoreVisible && _showMore.Contains(x, y)) OpenViewerRequested?.Invoke(this, EventArgs.Empty); }
    public bool Scroll(int delta)
    {
        if (delta == 0) return false;
        int before = _firstRow;
        bool hadHover = _hovered.HasValue;
        _hovered = null; _hoverLength = 0;
        FirstVisibleRowIndex += delta > 0 ? -1 : 1;
        if (before == _firstRow && hadHover) Changed?.Invoke(this, EventArgs.Empty);
        return before != _firstRow;
    }
    public string CopyAll() => Options.DisplayMode == HexDisplayMode.Word
        ? HexWordLayout.Format(new ListHexDataSource(Data), 0, Data.Count, DisplayColumns, Options.GroupSize, Options.VisualByteOffset, Options.ByteDisplayOrder)
        : FormatHex.BytesToHexBlockString(Data);
    public string CopyHovered() => _hovered is int start ? FormatHex.BytesToHexString(Data.Skip(start).Take(_hoverLength).ToArray()) : "";
    public void Fill(byte value)
    {
        if (!Options.IsEditable) return;
        Data = Enumerable.Repeat(value, Data.Count).ToArray(); DataChangedExternally?.Invoke(this, EventArgs.Empty);
    }
    public void ReplaceData(byte[] bytes)
    {
        if (!Options.IsEditable) return;
        Data = bytes; DataChangedExternally?.Invoke(this, EventArgs.Empty);
    }
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
