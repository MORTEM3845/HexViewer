using MessageBox = System.Windows.MessageBox;
using HexViewer.Control.Services;
using HexViewer.Items;
using HexViewer.Items.Enums;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HexViewer.Control.HexEditor;


public sealed class HexEditorSelectionChangedEventArgs : EventArgs
{
    public HexEditorSelectionChangedEventArgs(int caretIndex, int selectionStart, int selectionLength)
    {
        CaretIndex = caretIndex;
        SelectionStart = selectionStart;
        SelectionLength = selectionLength;
    }

    public int CaretIndex { get; }
    public int SelectionStart { get; }
    public int SelectionLength { get; }
}

internal sealed class HexEditUndoItem
{
    public HexEditUndoItem(int index, byte[] removed, byte[] inserted, int oldCaret, int newCaret,
        bool oldHighNibble, bool newHighNibble, long oldRevision, long newRevision)
    {
        Index = index;
        Removed = removed;
        Inserted = inserted;
        OldCaret = oldCaret;
        NewCaret = newCaret;
        OldHighNibble = oldHighNibble;
        NewHighNibble = newHighNibble;
        OldRevision = oldRevision;
        NewRevision = newRevision;
    }

    public int Index { get; }
    public byte[] Removed { get; }
    public byte[] Inserted { get; }
    public int OldCaret { get; }
    public int NewCaret { get; }
    public bool OldHighNibble { get; }
    public bool NewHighNibble { get; }
    public long OldRevision { get; }
    public long NewRevision { get; }
    public long MemorySize => Removed.LongLength + Inserted.LongLength;
}

public sealed partial class HexEditor : FrameworkElement
{
    private const double PaddingLeft = 10;
    private const double PaddingTop = 6;
    private const double OffsetGap = 14;
    private const double AsciiGap = 22;
    private const int MaxUndoCount = 100;
    private const long MaxUndoBytes = 64L * 1024 * 1024;
    private const string BinaryClipboardFormat = "HexViewer.BinaryBytes";
    private const string RawClipboardFormat = "HexViewer.RawBytes";

    private readonly List<byte> _bytes = new();
    private readonly HashSet<int> _changedIndexes = new();
    private HashSet<int> _readOnlyBytes = new();

    /// <summary>Задаёт индексы байтов, защищённых от пользовательских изменений. SetBytes может обновлять их.</summary>
    public void SetReadOnlyBytes(IEnumerable<int> indexes)
    {
        var protectedBytes = indexes.ToHashSet();
        if (protectedBytes.Any(index => index < 0)) throw new ArgumentOutOfRangeException(nameof(indexes));
        if (_readOnlyBytes.SetEquals(protectedBytes)) return;
        _readOnlyBytes = protectedBytes;
        _pendingHexIndex = -1;
        InvalidateVisual();
    }

    public bool IsByteReadOnly(int index) => _readOnlyBytes.Contains(index);

    private bool CanReplace(int index, int removeCount, int insertCount) => !_readOnlyBytes.Any(locked =>
        removeCount == insertCount ? locked >= index && locked < index + removeCount : locked >= index);
    private readonly Stack<HexEditUndoItem> _undo = new();
    private readonly Stack<HexEditUndoItem> _redo = new();
    private readonly Typeface _typeface = new("Consolas");
    private readonly Dictionary<(string Text, Brush Brush), FormattedText> _formattedTextCache = new();

    private int _firstVisibleRowIndex;
    private int _caretIndex;
    private int _selectionAnchor = -1;
    private int _selectionStart = -1;
    private int _selectionLength;
    private bool _editHighNibble = true;
    private bool _isMouseSelecting;
    private int _pendingHexIndex = -1;
    private long _revision;
    private long _nextRevision;
    private long _savedRevision;
    private byte[] _savedBytes = Array.Empty<byte>();
    private double _charWidth;
    private double _rowHeight = 22;
    private double _offsetWidth;
    private double _hexStartX;
    private double _asciiStartX;
    private double _contentWidth;
    private double _ppd = 1;
    private int? _contextIndex;
    private HexEditorArea _activeArea = HexEditorArea.Hex;

    private MenuItem? _copyMenuItem;
    private MenuItem? _cutMenuItem;
    private MenuItem? _pasteMenuItem;
    private MenuItem? _deleteMenuItem;
    private MenuItem? _fillZerosMenuItem;
    public bool IsModified { get; private set; }
    public int CaretIndex => _caretIndex;
    public int SelectionStart => _selectionStart;
    public int SelectionLength => _selectionLength;
    public bool HasSelection => _selectionLength > 0;
    public int ByteCount => _bytes.Count;

    public event EventHandler? DataEdited;
    public event EventHandler? ModifiedChanged;
    public event EventHandler? InsertModeChanged;
    public event EventHandler<int>? FirstVisibleRowChanged;
    public event EventHandler<int>? TotalRowsChanged;
    public event EventHandler<HexEditorSelectionChangedEventArgs>? SelectionChanged;

    public HexEditor()
    {
        Focusable = true;
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        ContextMenu = CreateContextMenu();
        ContextMenuOpening += (_, _) => UpdateContextMenuState();
        ContextMenu.Closed += (_, _) => _contextIndex = null;

        Loaded += (_, _) => UpdateMetrics();
        SizeChanged += (_, _) => { UpdateMetrics(ActualWidth); UpdateVisibleRowsFromHeight(); ClampScroll(); InvalidateVisual(); };
    }

    /// <summary>Встроенный редактор без подложки и фоновой подсветки текущего или защищённого байта.</summary>
    public bool IsInline
    {
        get => (bool)GetValue(IsInlineProperty);
        set => SetValue(IsInlineProperty, value);
    }

    public static readonly DependencyProperty IsInlineProperty = DependencyProperty.Register(
        nameof(IsInline), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Word показывает слитное HEX-слово без побайтовой подсветки и подсказок.</summary>
    public HexDisplayMode DisplayMode
    {
        get => (HexDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(HexDisplayMode), typeof(HexEditor),
        new FrameworkPropertyMetadata(HexDisplayMode.Bytes,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            OnLayoutPropertyChanged), value => value is HexDisplayMode mode && Enum.IsDefined(mode));

    private bool WordLayout => DisplayMode == HexDisplayMode.Word;
    private bool DisplayAscii => ShowAscii && !WordLayout;

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        if (WordLayout) e.Handled = true;
        else base.OnToolTipOpening(e);
    }

    public IList<byte>? Data
    {
        get => (IList<byte>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(nameof(Data), typeof(IList<byte>), typeof(HexEditor), new FrameworkPropertyMetadata(null, OnDataChanged));

    public bool ShowAscii
    {
        get => (bool)GetValue(ShowAsciiProperty);
        set => SetValue(ShowAsciiProperty, value);
    }

    public static readonly DependencyProperty ShowAsciiProperty = DependencyProperty.Register(
        nameof(ShowAscii), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public bool ShowHeader
    {
        get => (bool)GetValue(ShowHeaderProperty);
        set => SetValue(ShowHeaderProperty, value);
    }

    public static readonly DependencyProperty ShowHeaderProperty = DependencyProperty.Register(
        nameof(ShowHeader), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public bool ShowOffsets
    {
        get => (bool)GetValue(ShowOffsetsProperty);
        set => SetValue(ShowOffsetsProperty, value);
    }

    public static readonly DependencyProperty ShowOffsetsProperty = DependencyProperty.Register(
        nameof(ShowOffsets), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public HexTextEncoding TextEncoding
    {
        get => (HexTextEncoding)GetValue(TextEncodingProperty);
        set => SetValue(TextEncodingProperty, value);
    }

    public static readonly DependencyProperty TextEncodingProperty = DependencyProperty.Register(
        nameof(TextEncoding), typeof(HexTextEncoding), typeof(HexEditor),
        new FrameworkPropertyMetadata(HexTextEncoding.Ascii, FrameworkPropertyMetadataOptions.AffectsRender,
            null, CoerceTextEncoding));

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(HexEditor),
        new FrameworkPropertyMetadata(32, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public bool AutoFitColumnsToWidth
    {
        get => (bool)GetValue(AutoFitColumnsToWidthProperty);
        set => SetValue(AutoFitColumnsToWidthProperty, value);
    }
    public static readonly DependencyProperty AutoFitColumnsToWidthProperty = DependencyProperty.Register(
        nameof(AutoFitColumnsToWidth), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    private int _fittedColumns = 32;
    private double _layoutWidth = double.PositiveInfinity;

    public int GroupSize
    {
        get => (int)GetValue(GroupSizeProperty);
        set => SetValue(GroupSizeProperty, value);
    }

    public static readonly DependencyProperty GroupSizeProperty = DependencyProperty.Register(
        nameof(GroupSize), typeof(int), typeof(HexEditor),
        new FrameworkPropertyMetadata(4, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public double GroupGapChars
    {
        get => (double)GetValue(GroupGapCharsProperty);
        set => SetValue(GroupGapCharsProperty, value);
    }

    public static readonly DependencyProperty GroupGapCharsProperty = DependencyProperty.Register(
        nameof(GroupGapChars), typeof(double), typeof(HexEditor),
        new FrameworkPropertyMetadata(1.1, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public bool CompactShortData
    {
        get => (bool)GetValue(CompactShortDataProperty);
        set => SetValue(CompactShortDataProperty, value);
    }

    public static readonly DependencyProperty CompactShortDataProperty = DependencyProperty.Register(
        nameof(CompactShortData), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public int VisibleRows
    {
        get => (int)GetValue(VisibleRowsProperty);
        set => SetValue(VisibleRowsProperty, value);
    }

    public static readonly DependencyProperty VisibleRowsProperty = DependencyProperty.Register(
        nameof(VisibleRows), typeof(int), typeof(HexEditor),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutPropertyChanged));

    public bool InsertMode
    {
        get => (bool)GetValue(InsertModeProperty);
        set => SetValue(InsertModeProperty, value);
    }

    public static readonly DependencyProperty InsertModeProperty = DependencyProperty.Register(
        nameof(InsertMode), typeof(bool), typeof(HexEditor),
        new FrameworkPropertyMetadata(false, (d, _) => ((HexEditor)d).InsertModeChanged?.Invoke(d, EventArgs.Empty)));

    public int FirstVisibleRowIndex
    {
        get => _firstVisibleRowIndex;
        set
        {
            int newValue = Math.Clamp(value, 0, GetMaxFirstRow());
            if (_firstVisibleRowIndex == newValue) return;

            _firstVisibleRowIndex = newValue;
            FirstVisibleRowChanged?.Invoke(this, _firstVisibleRowIndex);
            InvalidateVisual();
        }
    }

    public void MarkSaved()
    {
        _savedRevision = _revision;
        _savedBytes = _bytes.ToArray();
        RefreshChangedIndexes();
        UpdateModifiedState();
        InvalidateVisual();
    }

    public byte[] GetBytes() => _bytes.ToArray();

    public void SetBytes(IEnumerable<byte>? bytes, bool clearModified = true)
    {
        _bytes.Clear();
        if (bytes != null) _bytes.AddRange(bytes);

        _caretIndex = Math.Clamp(_caretIndex, 0, _bytes.Count);
        ClearSelection(false);
        _editHighNibble = true;
        _changedIndexes.Clear();
        _undo.Clear();
        _redo.Clear();
        _pendingHexIndex = -1;

        if (clearModified)
        {
            _revision = ++_nextRevision;
            _savedRevision = _revision;
            _savedBytes = _bytes.ToArray();
            UpdateModifiedState();
        }

        ClampScroll();
        NotifyStateChanged();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SelectRange(int start, int length)
    {
        if (_bytes.Count == 0)
        {
            ClearSelection();
            MoveCaretTo(0);
            return;
        }

        start = Math.Clamp(start, 0, _bytes.Count);
        length = Math.Clamp(length, 0, _bytes.Count - start);
        _selectionStart = length == 0 ? -1 : start;
        _selectionLength = length;
        _selectionAnchor = length == 0 ? -1 : start;
        MoveCaretTo(start + length, ensureVisible: true, resetNibble: true, clearSelection: false);
        RaiseSelectionChanged();
        InvalidateVisual();
    }

    public void SelectAll()
    {
        if (_bytes.Count == 0) return;

        _selectionStart = 0;
        _selectionLength = _bytes.Count;
        _selectionAnchor = 0;
        MoveCaretTo(_bytes.Count, ensureVisible: true, resetNibble: true, clearSelection: false);
        RaiseSelectionChanged();
        InvalidateVisual();
    }

    public void ScrollToByte(int index, bool center = true)
    {
        if (_bytes.Count == 0) { FirstVisibleRowIndex = 0; return; }

        index = Math.Clamp(index, 0, _bytes.Count);
        int row = GetDataRow(index);
        FirstVisibleRowIndex = center ? row - Math.Max(0, VisibleRows / 2) : row;
    }

    public void GoToByte(int index)
    {
        MoveCaretTo(Math.Clamp(index, 0, _bytes.Count));
        ScrollToByte(_caretIndex, center: true);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        UpdateMetrics(availableSize.Width);
        int rows = Math.Max(1, VisibleRows);
        return new Size(_contentWidth + PaddingLeft * 2, PaddingTop * 2 + _rowHeight * rows + HeaderHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        UpdateMetrics();
        dc.DrawRectangle(IsInline ? Brushes.Transparent : HexRenderResources.BackgroundBrush,
            null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (ShowHeader && !WordLayout) DrawHeader(dc);
        DrawRows(dc);
        DrawCaret(dc);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.ChangedButton != MouseButton.Left)
            return;

        if (TryHitByte(e.GetPosition(this), out int index, out var area, out bool lowNibble))
        {
            _activeArea = area;
            _editHighNibble = !lowNibble;

            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                ExtendSelectionTo(index);
            else
            {
                ClearSelection(false);
                _selectionAnchor = index;
                MoveCaretTo(index, ensureVisible: false, resetNibble: false, clearSelection: false);
            }

            _isMouseSelecting = true;
            CaptureMouse();
            InvalidateVisual();
        }
        else
        {
            ClearSelection();
        }

        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();

        if (TryHitByte(e.GetPosition(this), out int index, out var area, out bool lowNibble))
        {
            _activeArea = area;
            _editHighNibble = !lowNibble;
            _contextIndex = index;

            if (!HasSelection)
                MoveCaretTo(index, ensureVisible: false, resetNibble: false);
        }
        else
        {
            _contextIndex = _caretIndex;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!WordLayout && _readOnlyBytes.Count > 0)
        {
            bool locked = TryHitByte(e.GetPosition(this), out int hovered, out _, out _) && IsByteReadOnly(hovered);
            Cursor = locked ? Cursors.Arrow : Cursors.IBeam;
            ToolTip = locked ? $"Байт {hovered}: только чтение" : null;
        }

        if (!_isMouseSelecting || e.LeftButton != MouseButtonState.Pressed)
            return;

        if (TryHitByte(e.GetPosition(this), out int index, out var area, out bool lowNibble))
        {
            _activeArea = area;
            _editHighNibble = !lowNibble;
            ExtendSelectionTo(index);
            InvalidateVisual();
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.ChangedButton != MouseButton.Left)
            return;

        _isMouseSelecting = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        int previousRow = FirstVisibleRowIndex;
        FirstVisibleRowIndex += e.Delta > 0 ? -3 : 3;
        e.Handled = FirstVisibleRowIndex != previousRow;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

        if (ctrl && e.Key == Key.A) { SelectAll(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.C) { CopySelection(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.X) { CutSelection(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.V) { PasteFromClipboard(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; return; }

        int columns = RowColumns;
        switch (e.Key)
        {
            case Key.Left: MoveHorizontal(-1, shift); e.Handled = true; break;
            case Key.Right: MoveHorizontal(1, shift); e.Handled = true; break;
            case Key.Up: MoveByBytes(-columns, shift); e.Handled = true; break;
            case Key.Down: MoveByBytes(columns, shift); e.Handled = true; break;
            case Key.PageUp: MoveByBytes(-columns * Math.Max(1, VisibleRows), shift); e.Handled = true; break;
            case Key.PageDown: MoveByBytes(columns * Math.Max(1, VisibleRows), shift); e.Handled = true; break;
            case Key.Home: MoveAbsolute((_caretIndex / columns) * columns, shift); e.Handled = true; break;
            case Key.End: MoveAbsolute(Math.Min(_bytes.Count, (_caretIndex / columns) * columns + columns - 1), shift); e.Handled = true; break;
            case Key.Insert: InsertMode = !InsertMode; e.Handled = true; break;
            case Key.Delete: DeleteSelectionOrNextByte(); e.Handled = true; break;
            case Key.Back: Backspace(); e.Handled = true; break;
            case Key.Tab:
                if (DisplayAscii) _activeArea = _activeArea == HexEditorArea.Hex ? HexEditorArea.Ascii : HexEditorArea.Hex;
                else _activeArea = HexEditorArea.Hex;
                InvalidateVisual();
                e.Handled = true;
                break;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text))
            return;

        if (_activeArea == HexEditorArea.Hex && TryGetHexValue(e.Text[0], out int hex))
        {
            EnterHexDigit(hex);
            e.Handled = true;
            return;
        }

        if (DisplayAscii && _activeArea == HexEditorArea.Ascii &&
            HexTextCodec.TryEncode(e.Text, TextEncoding, out byte[] encoded))
        {
            ReplaceByInput(encoded);
            e.Handled = true;
        }
    }

    public void CopySelection() => TryCopySelection();

    private bool TryCopySelection()
    {
        if (!HasSelection) return false;
        try
        {
            byte[] selected = _bytes.GetRange(_selectionStart, _selectionLength).ToArray();
            var data = new DataObject();
            data.SetData(BinaryClipboardFormat, selected);
            data.SetData(RawClipboardFormat, new MemoryStream(selected), autoConvert: false);
            data.SetText(WordLayout ? Convert.ToHexString(selected) : ToHexBlock(selected, 0, selected.Length, RowColumns));
            Clipboard.SetDataObject(data, true);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show("Буфер обмена временно недоступен.", "HEX редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void CutSelection()
    {
        if (!HasSelection) return;
        if (!TryCopySelection()) return;
        DeleteSelectionOrNextByte();
    }

    public void PasteFromClipboard()
    {
        byte[] bytes;
        try
        {
            if (Clipboard.GetData(BinaryClipboardFormat) is byte[] binary)
                bytes = binary;
            else if (Clipboard.GetData(RawClipboardFormat) is MemoryStream raw)
                bytes = raw.ToArray();
            else
            {
                string text = Clipboard.GetText();
                if (text.Length == 0) return;
                if (_activeArea == HexEditorArea.Hex)
                {
                    if (!HexParser.TryParseBytes(text, out bytes))
                    {
                        MessageBox.Show("Буфер не содержит корректную последовательность HEX-байтов.", "HEX редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
                else
                {
                    if (!HexTextCodec.TryEncode(text, TextEncoding, out bytes))
                    {
                        MessageBox.Show("Текст нельзя представить в выбранной кодировке.", "HEX редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show("Буфер обмена временно недоступен.", "HEX редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (bytes.Length == 0) return;
        ReplaceByInput(bytes);
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;

        var item = _undo.Peek();
        if (!CanReplace(item.Index, item.Inserted.Length, item.Removed.Length)) return;
        _undo.Pop();
        ApplyUndoItem(item, undo: true);
        _redo.Push(item);
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;

        var item = _redo.Peek();
        if (!CanReplace(item.Index, item.Removed.Length, item.Inserted.Length)) return;
        _redo.Pop();
        ApplyUndoItem(item, undo: false);
        _undo.Push(item);
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu { FontSize = 15 };
        _valueMenuItem = CreateMenuItem("Редактировать число…", OpenValueEditor);
        menu.Items.Add(_valueMenuItem);
        menu.Items.Add(new Separator());

        var insertMenu = new MenuItem { Header = "Вставить пустые байты здесь" };
        insertMenu.Items.Add(CreateMenuItem("1 байт 00", () => InsertZerosAtContext(1)));
        insertMenu.Items.Add(CreateMenuItem("2 байта 00", () => InsertZerosAtContext(2)));
        insertMenu.Items.Add(CreateMenuItem("4 байта 00", () => InsertZerosAtContext(4)));
        insertMenu.Items.Add(CreateMenuItem("8 байт 00", () => InsertZerosAtContext(8)));
        insertMenu.Items.Add(CreateMenuItem("16 байт 00", () => InsertZerosAtContext(16)));
        insertMenu.Items.Add(CreateMenuItem("32 байта 00", () => InsertZerosAtContext(32)));

        _copyMenuItem = CreateMenuItem("Копировать выделение", CopySelection);
        _cutMenuItem = CreateMenuItem("Вырезать выделение", CutSelection);
        _pasteMenuItem = CreateMenuItem("Вставить из буфера", PasteFromClipboard);
        _deleteMenuItem = CreateMenuItem("Удалить выделение/байт", DeleteSelectionOrNextByte);
        _fillZerosMenuItem = CreateMenuItem("Заполнить выделение нулями", FillSelectionWithZeros);

        var groupMenu = new MenuItem { Header = "Визуальная группировка" };
        groupMenu.Items.Add(CreateMenuItem("Без группировки", () => GroupSize = 0));
        groupMenu.Items.Add(CreateMenuItem("По 2 байта", () => GroupSize = 2));
        groupMenu.Items.Add(CreateMenuItem("По 4 байта", () => GroupSize = 4));
        groupMenu.Items.Add(CreateMenuItem("По 8 байт", () => GroupSize = 8));

        var asciiMenu = CreateMenuItem("Показать/скрыть ASCII", () => ShowAscii = !ShowAscii);

        menu.Items.Add(insertMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(_copyMenuItem);
        menu.Items.Add(_cutMenuItem);
        menu.Items.Add(_pasteMenuItem);
        menu.Items.Add(_deleteMenuItem);
        menu.Items.Add(_fillZerosMenuItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(groupMenu);
        menu.Items.Add(CreateValueByteOrderMenu());
        menu.Items.Add(asciiMenu);

        return menu;
    }

    private static MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void UpdateContextMenuState()
    {
        var range = ValueRange();
        if (_valueMenuItem != null) _valueMenuItem.IsEnabled = range.Length is >= 1 and <= 8 && CanReplace(range.Start, range.Length, range.Length);
        if (_copyMenuItem != null) _copyMenuItem.IsEnabled = HasSelection;
        if (_cutMenuItem != null) _cutMenuItem.IsEnabled = HasSelection;
        if (_fillZerosMenuItem != null) _fillZerosMenuItem.IsEnabled = HasSelection;
        if (_deleteMenuItem != null) _deleteMenuItem.IsEnabled = HasSelection || _caretIndex < _bytes.Count;
        if (_pasteMenuItem != null)
        {
            try { _pasteMenuItem.IsEnabled = Clipboard.ContainsText() || Clipboard.ContainsData(BinaryClipboardFormat) || Clipboard.ContainsData(RawClipboardFormat); }
            catch (System.Runtime.InteropServices.ExternalException) { _pasteMenuItem.IsEnabled = false; }
        }
    }

    private int GetContextIndex() => Math.Clamp(_contextIndex ?? _caretIndex, 0, _bytes.Count);

    private void InsertZerosAtContext(int count)
    {
        if (count <= 0) return;

        int index = GetContextIndex();
        if (!ReplaceRange(index, 0, new byte[count], createUndo: true)) return;
        ClearSelection(false);
        MoveCaretTo(index + count, resetNibble: true, clearSelection: false);
        FinishEdit();
    }

    private void FillSelectionWithZeros()
    {
        if (!HasSelection) return;

        int start = _selectionStart;
        int length = _selectionLength;
        if (!ReplaceRange(start, length, new byte[length], createUndo: true)) return;
        ClearSelection(false);
        MoveCaretTo(start, resetNibble: true, clearSelection: false);
        FinishEdit();
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HexEditor editor)
            editor.SetBytes(e.NewValue as IEnumerable<byte>);
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not HexEditor editor) return;

        if (editor.Columns < 1) editor.SetCurrentValue(ColumnsProperty, 1);
        if (editor.VisibleRows < 1) editor.SetCurrentValue(VisibleRowsProperty, 1);
        if (editor.GroupSize < 0) editor.SetCurrentValue(GroupSizeProperty, 0);
        if (editor.GroupGapChars < 0) editor.SetCurrentValue(GroupGapCharsProperty, 0d);
        if (!editor.DisplayAscii && editor._activeArea == HexEditorArea.Ascii) editor._activeArea = HexEditorArea.Hex;
        if (editor.WordLayout) { editor.ToolTip = null; editor.Cursor = Cursors.IBeam; }

        editor.ClampScroll();
        editor.NotifyStateChanged();
        editor.InvalidateMeasure();
        editor.InvalidateVisual();
    }

    private static object CoerceTextEncoding(DependencyObject _, object value) =>
        value is HexTextEncoding encoding && Enum.IsDefined(encoding) ? encoding : HexTextEncoding.Ascii;

    private void DrawHeader(DrawingContext dc)
    {
        double y = PaddingTop;
        int columns = HeaderColumns;

        for (int col = 0; col < columns; col++)
        {
            var text = CreateText(col.ToString("X2"), HexRenderResources.HeaderBrush);
            double x = GetHexX(col) + (_charWidth * 2 - text.Width) / 2;
            dc.DrawText(text, new Point(x, y));
        }

        if (ShowAscii)
            dc.DrawText(CreateText("ASCII", HexRenderResources.HeaderBrush), new Point(_asciiStartX, y));

        double lineY = PaddingTop + _rowHeight;
        dc.DrawLine(HexRenderResources.GridPen, new Point(PaddingLeft, lineY), new Point(Math.Max(ActualWidth - PaddingLeft, _contentWidth), lineY));
    }

    private void DrawRows(DrawingContext dc)
    {
        int rows = Math.Min(VisibleRows, Math.Max(1, GetTotalRows()));

        for (int row = 0; row < rows; row++)
        {
            int realRow = FirstVisibleRowIndex + row;
            int rowOffset = realRow * RowColumns;
            int columns = GetColumnsForRow(rowOffset);
            double y = PaddingTop + HeaderHeight + _rowHeight * row;
            string[] asciiCells = DisplayAscii
                ? HexTextCodec.DecodeCells(_bytes, rowOffset, Math.Min(columns, Math.Max(0, _bytes.Count - rowOffset)), TextEncoding)
                : Array.Empty<string>();

            if (ShowOffsets && !WordLayout)
            {
                var offsetText = CreateText(rowOffset.ToString($"X{OffsetDigits}"), HexRenderResources.OffsetBrush);
                dc.DrawText(offsetText, new Point(PaddingLeft + _offsetWidth - OffsetGap - offsetText.Width,
                    y - 1 + (_rowHeight - offsetText.Height) / 2));
            }

            if (WordLayout)
            {
                var count = Math.Min(columns, Math.Max(0, _bytes.Count - rowOffset));
                if (HasSelection)
                {
                    var start = Math.Max(rowOffset, _selectionStart);
                    var end = Math.Min(rowOffset + count, _selectionStart + _selectionLength);
                    if (end > start) dc.DrawRectangle(HexRenderResources.SelectionBrush, null,
                        new Rect(GetHexX(start - rowOffset), y - 1, (end - start) * ByteCellWidth, _rowHeight));
                }
                var text = CreateText(Convert.ToHexString(_bytes.Skip(rowOffset).Take(count).ToArray()), HexRenderResources.TextBrush);
                dc.DrawText(text, new Point(GetHexX(0), y - 1 + (_rowHeight - text.Height) / 2));
                continue;
            }

            for (int col = 0; col < columns; col++)
            {
                int index = rowOffset + col;
                if (index >= _bytes.Count) break;

                DrawByteBackground(dc, index, col, y);

                Brush brush = IsByteReadOnly(index) ? Brushes.DimGray
                    : _changedIndexes.Contains(index) ? HexRenderResources.ChangedBrush : HexRenderResources.TextBrush;
                var hex = CreateText(_bytes[index].ToString("X2"), brush);
                dc.DrawText(hex, new Point(GetHexX(col), y - 1 + (_rowHeight - hex.Height) / 2));

                if (ShowAscii)
                {
                    string displayText = asciiCells[col];
                    var ascii = CreateText(displayText,
                        displayText is "." or "" ? HexRenderResources.AsciiNonPrintableBrush : brush);
                    dc.DrawText(ascii, new Point(_asciiStartX + col * _charWidth,
                        y - 1 + (_rowHeight - ascii.Height) / 2));
                }
            }
        }
    }

    private void DrawByteBackground(DrawingContext dc, int index, int col, double y)
    {
        bool selected = HasSelection && index >= _selectionStart && index < _selectionStart + _selectionLength;
        if (IsInline && !selected) return;
        bool current = index == Math.Min(_caretIndex, Math.Max(0, _bytes.Count - 1)) && IsKeyboardFocusWithin;
        bool locked = IsByteReadOnly(index);
        if (!selected && !current && !locked) return;

        Brush brush = IsInline ? HexRenderResources.SelectionBrush
            : locked ? Brushes.Gainsboro : selected ? HexRenderResources.SelectionBrush : HexRenderResources.CurrentByteBrush;
        double hx = GetHexX(col) - 2;
        dc.DrawRectangle(brush, null, new Rect(hx, y - 1, _charWidth * 2 + 4, _rowHeight));

        if (ShowAscii)
        {
            double ax = _asciiStartX + col * _charWidth - 1;
            dc.DrawRectangle(brush, null, new Rect(ax, y - 1, _charWidth + 2, _rowHeight));
        }
    }

    private void DrawCaret(DrawingContext dc)
    {
        if (!IsKeyboardFocusWithin || HasSelection || IsByteReadOnly(_caretIndex) || _bytes.Count == 0 && _caretIndex > 0)
            return;

        int columns = RowColumns;
        int index = Math.Clamp(_caretIndex, 0, _bytes.Count);
        int row = GetDataRow(index);
        int col = index - row * columns;

        if (row < FirstVisibleRowIndex || row >= FirstVisibleRowIndex + VisibleRows)
            return;

        double y = PaddingTop + HeaderHeight + _rowHeight * (row - FirstVisibleRowIndex);
        double x = _activeArea == HexEditorArea.Ascii && DisplayAscii
            ? _asciiStartX + col * _charWidth
            : col == columns ? GetHexX(col - 1) + _charWidth * 2
            : GetHexX(col) + (_editHighNibble ? 0 : _charWidth);

        dc.DrawLine(HexRenderResources.CaretPen, new Point(x, y - 1), new Point(x, y + _rowHeight - 2));
    }

    private void EnterHexDigit(int hex)
    {
        // В пустом буфере и в позиции добавления младшей тетрады ещё нет.
        if (_caretIndex >= _bytes.Count) _editHighNibble = true;
        if (_editHighNibble)
        {
            int firstIndex = HasSelection ? _selectionStart : _caretIndex;
            int removeCount = HasSelection ? _selectionLength : InsertMode ? 0 : Math.Min(1, _bytes.Count - firstIndex);
            byte firstValue = removeCount == 1 && !HasSelection
                ? (byte)((_bytes[firstIndex] & 0x0F) | (hex << 4))
                : (byte)(hex << 4);
            if (!ReplaceRange(firstIndex, removeCount, new[] { firstValue }, createUndo: true,
                newCaret: firstIndex, newHighNibble: false)) return;
            ClearSelection(false);
            _caretIndex = firstIndex;
            _editHighNibble = false;
            _pendingHexIndex = firstIndex;
            _activeArea = HexEditorArea.Hex;
            FinishEdit();
            return;
        }

        int index = Math.Clamp(_caretIndex, 0, Math.Max(0, _bytes.Count - 1));
        byte oldValue = _bytes[index];
        byte newValue = (byte)((oldValue & 0xF0) | hex);
        if (_pendingHexIndex == index && _undo.Count > 0)
        {
            var first = _undo.Pop();
            if (!ReplaceRange(index, 1, new[] { newValue }, createUndo: false)) return;
            var merged = new HexEditUndoItem(first.Index, first.Removed, new[] { newValue },
                first.OldCaret, index + 1, first.OldHighNibble, true, first.OldRevision, _revision);
            _undo.Push(merged);
        }
        else
        {
            if (!ReplaceRange(index, 1, new[] { newValue }, createUndo: true,
                newCaret: index + 1, newHighNibble: true)) return;
        }
        _pendingHexIndex = -1;
        MoveCaretTo(index + 1, resetNibble: true);

        _activeArea = HexEditorArea.Hex;
        FinishEdit();
    }

    private void ReplaceCurrentByte(byte value)
    {
        int index = HasSelection ? _selectionStart : _caretIndex;
        int removeCount = HasSelection ? _selectionLength : InsertMode ? 0 : Math.Min(1, _bytes.Count - index);
        if (!ReplaceRange(index, removeCount, new[] { value }, createUndo: true,
            newCaret: index + 1, newHighNibble: true)) return;
        ClearSelection(false);
        MoveCaretTo(index + 1, resetNibble: true, clearSelection: false);
        _activeArea = DisplayAscii ? HexEditorArea.Ascii : HexEditorArea.Hex;
        FinishEdit();
    }

    private void ReplaceByInput(byte[] bytes)
    {
        int index = HasSelection ? _selectionStart : _caretIndex;
        int removeCount = HasSelection ? _selectionLength : InsertMode ? 0 : Math.Min(bytes.Length, _bytes.Count - index);

        if (!ReplaceRange(index, removeCount, bytes, createUndo: true)) return;
        ClearSelection(false);
        MoveCaretTo(index + bytes.Length, resetNibble: true, clearSelection: false);
        FinishEdit();
    }

    private void DeleteSelectionOrNextByte()
    {
        if (HasSelection)
        {
            if (!CanReplace(_selectionStart, _selectionLength, 0)) return;
            DeleteSelectionOnly();
            FinishEdit();
            return;
        }

        if (_caretIndex >= _bytes.Count)
            return;

        if (!ReplaceRange(_caretIndex, 1, Array.Empty<byte>(), createUndo: true)) return;
        MoveCaretTo(_caretIndex);
        FinishEdit();
    }

    private void Backspace()
    {
        if (HasSelection)
        {
            if (!CanReplace(_selectionStart, _selectionLength, 0)) return;
            DeleteSelectionOnly();
            FinishEdit();
            return;
        }

        if (_caretIndex <= 0)
            return;

        if (!ReplaceRange(_caretIndex - 1, 1, Array.Empty<byte>(), createUndo: true)) return;
        MoveCaretTo(_caretIndex - 1);
        FinishEdit();
    }

    private void DeleteSelectionOnly()
    {
        if (!HasSelection) return;

        int start = _selectionStart;
        if (!ReplaceRange(start, _selectionLength, Array.Empty<byte>(), createUndo: true)) return;
        ClearSelection(false);
        MoveCaretTo(start, clearSelection: false);
    }

    private bool ReplaceRange(int index, int removeCount, IReadOnlyList<byte> insertBytes, bool createUndo,
        int? newCaret = null, bool newHighNibble = true)
    {
        index = Math.Clamp(index, 0, _bytes.Count);
        removeCount = Math.Clamp(removeCount, 0, _bytes.Count - index);
        if (!CanReplace(index, removeCount, insertBytes.Count)) return false;
        _pendingHexIndex = -1;

        byte[] removed = removeCount == 0 ? Array.Empty<byte>() : _bytes.GetRange(index, removeCount).ToArray();
        byte[] inserted = insertBytes.Count == 0 ? Array.Empty<byte>() : insertBytes.ToArray();
        int oldCaret = _caretIndex;
        bool oldHighNibble = _editHighNibble;
        long oldRevision = _revision;

        bool sameLength = removeCount == inserted.Length;
        if (sameLength)
        {
            for (int i = 0; i < inserted.Length; i++) _bytes[index + i] = inserted[i];
        }
        else
        {
            if (removeCount > 0) _bytes.RemoveRange(index, removeCount);
            if (inserted.Length > 0) _bytes.InsertRange(index, inserted);
        }

        int resultingCaret = Math.Clamp(newCaret ?? index + inserted.Length, 0, _bytes.Count);
        _revision = ++_nextRevision;
        if (createUndo && (removed.Length > 0 || inserted.Length > 0))
        {
            PushUndo(new HexEditUndoItem(index, removed, inserted, oldCaret, resultingCaret,
                oldHighNibble, newHighNibble, oldRevision, _revision));
            _redo.Clear();
        }

        if (sameLength) RefreshChangedRange(index, inserted.Length);
        else RefreshChangedIndexes();
        UpdateModifiedState();
        NotifyStateChanged();
        InvalidateMeasure();
        InvalidateVisual();
        return true;
    }

    private void ApplyUndoItem(HexEditUndoItem item, bool undo)
    {
        int removeCount = undo ? item.Inserted.Length : item.Removed.Length;
        byte[] insert = undo ? item.Removed : item.Inserted;
        int caret = undo ? item.OldCaret : item.NewCaret;

        if (removeCount > 0) _bytes.RemoveRange(item.Index, Math.Min(removeCount, _bytes.Count - item.Index));
        if (insert.Length > 0) _bytes.InsertRange(item.Index, insert);

        _caretIndex = Math.Clamp(caret, 0, _bytes.Count);
        _editHighNibble = undo ? item.OldHighNibble : item.NewHighNibble;
        _revision = undo ? item.OldRevision : item.NewRevision;
        _pendingHexIndex = -1;
        ClearSelection(false);
        if (item.Inserted.Length == item.Removed.Length)
            RefreshChangedRange(item.Index, item.Inserted.Length);
        else
            RefreshChangedIndexes();
        UpdateModifiedState();
        NotifyStateChanged();
        DataEdited?.Invoke(this, EventArgs.Empty);
        EnsureCaretVisible();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void PushUndo(HexEditUndoItem item)
    {
        _undo.Push(item);
        var items = _undo.Take(MaxUndoCount).Reverse().ToList();
        long bytes = items.Sum(i => i.MemorySize);
        while (items.Count > 1 && bytes > MaxUndoBytes)
        {
            bytes -= items[0].MemorySize;
            items.RemoveAt(0);
        }
        _undo.Clear();
        foreach (var it in items) _undo.Push(it);
    }

    private void MoveAbsolute(int index, bool extendSelection)
    {
        if (extendSelection)
        {
            if (_selectionAnchor < 0) _selectionAnchor = _caretIndex;
            MoveCaretTo(index, ensureVisible: false, clearSelection: false);
            UpdateSelection(_selectionAnchor, _caretIndex);
            EnsureCaretVisible();
        }
        else
        {
            MoveCaretTo(index);
        }
    }

    private void MoveHorizontal(int direction, bool extendSelection)
    {
        if (direction > 0 && _caretIndex == _bytes.Count) return;

        if (_activeArea == HexEditorArea.Hex && !extendSelection)
        {
            if (direction > 0 && _editHighNibble) { _editHighNibble = false; InvalidateVisual(); return; }
            if (direction < 0 && !_editHighNibble) { _editHighNibble = true; InvalidateVisual(); return; }
            if (direction < 0 && _editHighNibble && _caretIndex > 0)
            {
                MoveCaretTo(_caretIndex - 1, resetNibble: false);
                _editHighNibble = false;
                InvalidateVisual();
                return;
            }
        }

        MoveByBytes(direction, extendSelection);
    }

    private void MoveByBytes(int delta, bool extendSelection)
    {
        int newIndex = Math.Clamp(_caretIndex + delta, 0, _bytes.Count);

        if (extendSelection)
        {
            if (_selectionAnchor < 0) _selectionAnchor = _caretIndex;
            MoveCaretTo(newIndex, ensureVisible: false, clearSelection: false);
            UpdateSelection(_selectionAnchor, newIndex);
            EnsureCaretVisible();
        }
        else
        {
            MoveCaretTo(newIndex);
        }
    }

    private void MoveCaretTo(int index, bool ensureVisible = true, bool resetNibble = true, bool clearSelection = true)
    {
        if (index != _caretIndex || resetNibble) _pendingHexIndex = -1;
        _caretIndex = Math.Clamp(index, 0, _bytes.Count);

        if (resetNibble) _editHighNibble = true;
        if (clearSelection) ClearSelection(false);
        if (ensureVisible) EnsureCaretVisible();

        RaiseSelectionChanged();
        InvalidateVisual();
    }

    private void ExtendSelectionTo(int index)
    {
        if (_selectionAnchor < 0) _selectionAnchor = _caretIndex;

        if (index >= _selectionAnchor)
        {
            int end = Math.Clamp(index + 1, 0, _bytes.Count);
            MoveCaretTo(end, ensureVisible: false, clearSelection: false, resetNibble: false);
            UpdateSelection(_selectionAnchor, end);
        }
        else
        {
            int start = Math.Clamp(index, 0, _bytes.Count);
            MoveCaretTo(start, ensureVisible: false, clearSelection: false, resetNibble: false);
            UpdateSelection(start, Math.Min(_bytes.Count, _selectionAnchor + 1));
        }
        EnsureCaretVisible();
    }

    private void UpdateSelection(int a, int b)
    {
        int start = Math.Min(a, b);
        int end = Math.Max(a, b);
        _selectionStart = end == start ? -1 : start;
        _selectionLength = end == start ? 0 : end - start;
        RaiseSelectionChanged();
    }

    private void ClearSelection(bool invalidate = true)
    {
        _selectionStart = -1;
        _selectionLength = 0;
        _selectionAnchor = -1;
        RaiseSelectionChanged();

        if (invalidate)
            InvalidateVisual();
    }

    private void EnsureCaretVisible()
    {
        // Правая граница выделения находится после байта; показываем сам выделенный байт.
        int visibleIndex = HasSelection && _caretIndex == _selectionStart + _selectionLength
            ? _caretIndex - 1 : _caretIndex;
        int row = GetDataRow(visibleIndex);
        if (row < FirstVisibleRowIndex) FirstVisibleRowIndex = row;
        else if (row >= FirstVisibleRowIndex + VisibleRows) FirstVisibleRowIndex = row - VisibleRows + 1;
    }

    private void ClampScroll() => FirstVisibleRowIndex = _firstVisibleRowIndex;

    // Позиция после последнего байта остаётся справа от данных, без пустой строки.
    private int GetDataRow(int index) => Math.Min(index, Math.Max(0, _bytes.Count - 1)) / RowColumns;

    private int GetTotalRows() => GetTotalRowsByColumns(RowColumns);
    private int GetMaxFirstRow() => Math.Max(0, GetTotalRows() - Math.Max(1, VisibleRows));
    private void NotifyStateChanged() => TotalRowsChanged?.Invoke(this, GetTotalRows());

    private void FinishEdit()
    {
        EnsureCaretVisible();
        NotifyStateChanged();
        DataEdited?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void SetModified(bool value)
    {
        if (IsModified == value) return;

        IsModified = value;
        ModifiedChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateModifiedState() => SetModified(_changedIndexes.Count != 0);

    private void RefreshChangedIndexes()
    {
        _changedIndexes.Clear();
        int count = Math.Max(_bytes.Count, _savedBytes.Length);
        for (int i = 0; i < count; i++)
        {
            if (i >= _bytes.Count || i >= _savedBytes.Length || _bytes[i] != _savedBytes[i])
                _changedIndexes.Add(i);
        }
    }

    private void RefreshChangedRange(int start, int length)
    {
        int end = Math.Min(_bytes.Count, start + length);
        for (int i = Math.Max(0, start); i < end; i++)
        {
            if (i >= _savedBytes.Length || _bytes[i] != _savedBytes[i]) _changedIndexes.Add(i);
            else _changedIndexes.Remove(i);
        }
    }

    private void RaiseSelectionChanged() =>
        SelectionChanged?.Invoke(this, new HexEditorSelectionChangedEventArgs(_caretIndex, _selectionStart, _selectionLength));

    private bool TryHitByte(Point pt, out int index, out HexEditorArea area, out bool lowNibble)
    {
        index = -1;
        area = _activeArea;
        lowNibble = false;

        double relativeY = pt.Y - PaddingTop - HeaderHeight;
        if (relativeY < 0) return false;
        int row = (int)(relativeY / _rowHeight);
        if (row < 0 || row >= VisibleRows)
            return false;

        int rowColumns = RowColumns;
        int realRow = FirstVisibleRowIndex + row;
        int rowOffset = realRow * rowColumns;
        int columns = GetColumnsForRow(rowOffset);

        if (pt.X >= _hexStartX && pt.X < _hexStartX + GetHexAreaWidth(columns))
        {
            for (int col = 0; col < columns; col++)
            {
                double x = GetHexX(col);
                if (pt.X < x || (WordLayout ? pt.X >= x + _charWidth * 2 : pt.X > x + _charWidth * 2 + 2))
                    continue;

                double cellX = pt.X - x;
                index = rowOffset + col;
                area = HexEditorArea.Hex;
                lowNibble = cellX >= _charWidth;
                break;
            }
        }
        else if (DisplayAscii && pt.X >= _asciiStartX && pt.X < _asciiStartX + columns * _charWidth)
        {
            int col = (int)((pt.X - _asciiStartX) / _charWidth);
            index = rowOffset + col;
            area = HexEditorArea.Ascii;
        }

        if (index < 0 || index > _bytes.Count)
            return false;

        if (index == _bytes.Count && _bytes.Count > 0)
            index = _bytes.Count - 1;

        return true;
    }

    private void UpdateVisibleRowsFromHeight()
    {
        if (ActualHeight <= _rowHeight + HeaderHeight) return;

        int rows = Math.Max(1, (int)((ActualHeight - PaddingTop * 2 - HeaderHeight) / _rowHeight));
        if (VisibleRows != rows) VisibleRows = rows;
    }

    private int RowColumns => AutoFitColumnsToWidth ? _fittedColumns : Math.Max(1, Columns);
    private double HeaderHeight => ShowHeader && !WordLayout ? _rowHeight : 0;

    private int HeaderColumns
    {
        get
        {
            if (!CompactShortData || _bytes.Count == 0)
                return RowColumns;

            return GetTotalRowsByColumns(RowColumns) == 1 ? Math.Min(RowColumns, _bytes.Count) : RowColumns;
        }
    }

    private int EffectiveGroupSize => WordLayout || GroupSize <= 0 ? 0 : GroupSize;
    private double GroupGapWidth => EffectiveGroupSize <= 0 ? 0 : Math.Max(0, GroupGapChars * _charWidth);
    private double ByteCellWidth => _charWidth * (WordLayout ? 2 : 3);

    private int GetColumnsForRow(int rowOffset)
    {
        if (!CompactShortData)
            return RowColumns;

        int left = _bytes.Count - rowOffset;
        if (left <= 0)
            return RowColumns;

        return Math.Min(RowColumns, left);
    }

    private static int GetTotalRowsByColumns(int count, int columns) =>
        count == 0 ? 1 : (int)Math.Ceiling(count / (double)Math.Max(1, columns));

    private int GetTotalRowsByColumns(int columns) => GetTotalRowsByColumns(_bytes.Count, columns);

    private void UpdateMetrics(double availableWidth = double.NaN)
    {
        if (!double.IsNaN(availableWidth)) _layoutWidth = availableWidth;
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (Math.Abs(_ppd - ppd) > double.Epsilon)
        {
            _ppd = ppd;
            _formattedTextCache.Clear();
        }
        var sample = CreateText("W", HexRenderResources.TextBrush);
        _charWidth = Math.Max(WordLayout ? 1 : 8, sample.WidthIncludingTrailingWhitespace);
        _rowHeight = Math.Max(20, sample.Height + 4);
        _offsetWidth = ShowOffsets && !WordLayout ? OffsetDigits * _charWidth + OffsetGap : 0;
        _hexStartX = PaddingLeft + _offsetWidth;

        int columnsBefore = _fittedColumns;
        _fittedColumns = AutoFitColumnsToWidth ? Core.HexColumnLayout.FitColumns(_layoutWidth, Columns,
            ByteCellWidth, EffectiveGroupSize, GroupGapWidth,
            PaddingLeft * 2 + _hexStartX + (DisplayAscii ? AsciiGap : 0), DisplayAscii ? _charWidth : 0) : Math.Max(1, Columns);
        if (columnsBefore != _fittedColumns)
        {
            ClampScroll();
            TotalRowsChanged?.Invoke(this, GetTotalRowsByColumns(RowColumns));
        }

        int columns = HeaderColumns;
        double hexWidth = GetHexAreaWidth(columns);

        _asciiStartX = _hexStartX + hexWidth + AsciiGap;
        _contentWidth = DisplayAscii ? _asciiStartX + columns * _charWidth : _hexStartX + hexWidth;
    }

    private double GetHexX(int col)
    {
        int groupSize = EffectiveGroupSize;
        int gapsBefore = groupSize <= 0 ? 0 : col / groupSize;
        return _hexStartX + col * ByteCellWidth + gapsBefore * GroupGapWidth;
    }

    private double GetHexAreaWidth(int columns)
    {
        int groupSize = EffectiveGroupSize;
        int gaps = groupSize <= 0 ? 0 : Math.Max(0, (columns - 1) / groupSize);
        return columns * ByteCellWidth + gaps * GroupGapWidth;
    }

    private bool IsIndexSelected(int index) => HasSelection && index >= _selectionStart && index < _selectionStart + _selectionLength;

    private int OffsetDigits
    {
        get
        {
            int maxOffset = Math.Max(0, _bytes.Count == 0 ? 0 : _bytes.Count - 1);
            return Math.Max(4, maxOffset.ToString("X", CultureInfo.InvariantCulture).Length);
        }
    }

    private FormattedText CreateText(string text, Brush brush)
    {
        var key = (text, brush);
        if (_formattedTextCache.TryGetValue(key, out var formatted)) return formatted;
        if (_formattedTextCache.Count >= 4096) _formattedTextCache.Clear();
        formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            _typeface, 15, brush, _ppd);
        _formattedTextCache[key] = formatted;
        return formatted;
    }

    private static bool TryGetHexValue(char ch, out int value)
    {
        if (ch >= '0' && ch <= '9') { value = ch - '0'; return true; }
        if (ch >= 'a' && ch <= 'f') { value = ch - 'a' + 10; return true; }
        if (ch >= 'A' && ch <= 'F') { value = ch - 'A' + 10; return true; }

        value = 0;
        return false;
    }

    private static string ToHexBlock(IReadOnlyList<byte> bytes, int start, int length, int columns)
    {
        columns = Math.Max(1, columns);
        var sb = new StringBuilder(length * 3);

        for (int i = 0; i < length; i++)
        {
            if (i > 0) sb.Append(i % columns == 0 ? Environment.NewLine : ' ');
            sb.Append(bytes[start + i].ToString("X2"));
        }

        return sb.ToString();
    }
}
