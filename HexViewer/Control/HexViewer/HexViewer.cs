using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HexViewer.Control.Services;
using HexViewer.Items.Enums;

namespace HexViewer.Control.HexViewer
{
    public partial class HexViewer : FrameworkElement
    {
        private const double OffsetToLinePadding = 8;
        private const double AsciiGap = 22;

        private int _firstVisibleRowIndex;
        private int? _hoveredGroupStartIndex;
        private int _hoveredGroupSize;
        private int _prevTotalRows;

        private bool _showMoreButtonHovered;
        private Rect _showMoreButtonRect;

        private readonly GeometryCache _geo = new();
        private readonly TextCache _texts = new();
        private readonly TooltipController _tooltip;
        private readonly ContextMenuController _menu;
        private readonly DefaultHexRenderer _hexRenderer = new DefaultHexRenderer();

        private MathHelper _math = new();

        private static readonly Brush TransparentBrush = Brushes.Transparent;
        private static readonly RoutedCommand ChangeWordSizeCommand = new();

        public Brush? Background
        {
            get => (Brush?)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly DependencyProperty BackgroundProperty =
            System.Windows.Controls.Control.BackgroundProperty.AddOwner(typeof(HexViewer),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly DependencyProperty ForegroundProperty =
            System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(HexViewer),
                new FrameworkPropertyMetadata(Brushes.Black,
                    FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender,
                    (owner, _) => ((HexViewer)owner)._lastDpi = 0));

        public HexViewer()
        {
            _tooltip = new TooltipController(this);

            // контекстное меню через контроллер
            _menu = new ContextMenuController(this);
            _menu.EditValue = OpenValueEditor;

            _menu.Bind(
             getData: () => _dataSource,
             getContextTetradStartIndex: () => null,
             getHoveredGroupStartIndex: () => _hoveredGroupStartIndex,
             getGroupSize: () => _hoveredGroupSize > 0 ? _hoveredGroupSize : GroupSize,
             setData: bytes =>
             {
                 SetCurrentValue(DataProperty, bytes);
                 FirstVisibleRowIndex = 0;
                 InvalidateMeasure();
                 InvalidateVisual();
                 RaiseDataChangedExternally();
             },
             setWordSize: size => SetCurrentValue(WordDisplaySizeProperty, size),
             getWordSize: () => WordDisplaySize,
             openEditor: OpenEditorWindow,
             openViewer: OpenFullViewerWindow,
             getCopyText: () => WordLayout && _dataSource is not null
                 ? Core.HexWordLayout.Format(_dataSource, 0, _dataSource.Count, DisplayColumns,
                     GroupSize, VisualByteOffset, ByteDisplayOrder) : null);

            _menu.Attach(IsLoadFromBinVisible && IsEditable);
            _menu.SetEditable(IsEditable);
            AddByteOrderMenu();

            CommandBindings.Add(new CommandBinding(ChangeWordSizeCommand, (_, e) =>
            {
                SetCurrentValue(WordDisplaySizeProperty, (WordSizeEnum)e.Parameter);
                e.Handled = true;
            }));
            AddWordSizeShortcuts(this);
            Focusable = true;
        }

        internal void AddWordSizeShortcuts(UIElement owner)
        {
            foreach (var (key, size) in new[]
            {
                (Key.D1, WordSizeEnum.Byte1),
                (Key.D2, WordSizeEnum.Byte2),
                (Key.D4, WordSizeEnum.Byte4)
            })
            {
                owner.InputBindings.Add(new KeyBinding(ChangeWordSizeCommand, key, ModifierKeys.Control)
                {
                    CommandTarget = this,
                    CommandParameter = size
                });
            }
        }

        public int Columns { get => (int)GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
        public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
            nameof(Columns), typeof(int), typeof(HexViewer), LayoutMetadata(32, CoercePositiveInt));

        public bool AutoFitColumnsToWidth
        {
            get => (bool)GetValue(AutoFitColumnsToWidthProperty);
            set => SetValue(AutoFitColumnsToWidthProperty, value);
        }
        public static readonly DependencyProperty AutoFitColumnsToWidthProperty = DependencyProperty.Register(
            nameof(AutoFitColumnsToWidth), typeof(bool), typeof(HexViewer),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnLayoutPropertyChanged));

        private int _fittedColumns = 32;
        private int DisplayColumns => AutoFitColumnsToWidth ? _fittedColumns : Columns;

        private void UpdateColumns(double width)
        {
            _fittedColumns = AutoFitColumnsToWidth ? CalculateFittedColumns(width) : Columns;
        }

        internal int CalculateFittedColumns(double width)
        {
            EnsureTextCache();
            int digits = Math.Max(3, Math.Max(0, (Data?.Count ?? 0) - 1).ToString("X").Length);
            double reserved = CompactLayout ? 0 : _texts.Create(new string('F', digits)).Width + OffsetToLinePadding;
            return Core.HexColumnLayout.FitColumns(width, Columns,
                DisplayCellWidth, GroupSize, DisplayGroupSpacing, reserved + (AsciiLayout ? AsciiGap : 0),
                AsciiLayout ? CalculatedAsciiCellWidth : 0);
        }

        public double CellWidth { get => (double)GetValue(CellWidthProperty); set => SetValue(CellWidthProperty, value); }
        public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
            nameof(CellWidth), typeof(double), typeof(HexViewer), LayoutMetadata(28d, CoercePositiveDouble));

        public double CellHeight { get => (double)GetValue(CellHeightProperty); set => SetValue(CellHeightProperty, value); }
        public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
            nameof(CellHeight), typeof(double), typeof(HexViewer), LayoutMetadata(20d, CoercePositiveDouble));

        public int GroupSize { get => (int)GetValue(GroupSizeProperty); set => SetValue(GroupSizeProperty, value); }
        public static readonly DependencyProperty GroupSizeProperty = DependencyProperty.Register(
            nameof(GroupSize), typeof(int), typeof(HexViewer), LayoutMetadata(4, CoercePositiveInt));

        public double GroupSpacing { get => (double)GetValue(GroupSpacingProperty); set => SetValue(GroupSpacingProperty, value); }
        public static readonly DependencyProperty GroupSpacingProperty = DependencyProperty.Register(
            nameof(GroupSpacing), typeof(double), typeof(HexViewer), LayoutMetadata(8d, CoerceNonNegativeDouble));

        public int VisibleRows { get => (int)GetValue(VisibleRowsProperty); set => SetValue(VisibleRowsProperty, value); }
        public static readonly DependencyProperty VisibleRowsProperty = DependencyProperty.Register(
            nameof(VisibleRows), typeof(int), typeof(HexViewer), LayoutMetadata(8, CoercePositiveInt));

        public bool AutoFitRowsToHeight
        {
            get => (bool)GetValue(AutoFitRowsToHeightProperty);
            set => SetValue(AutoFitRowsToHeightProperty, value);
        }

        public static readonly DependencyProperty AutoFitRowsToHeightProperty = DependencyProperty.Register(
            nameof(AutoFitRowsToHeight), typeof(bool), typeof(HexViewer),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnLayoutPropertyChanged));

        public int RowsStep { get => (int)GetValue(RowsStepProperty); set => SetValue(RowsStepProperty, value); }
        public static readonly DependencyProperty RowsStepProperty = DependencyProperty.Register(
            nameof(RowsStep), typeof(int), typeof(HexViewer), LayoutMetadata(8, CoercePositiveInt));

        public bool ShowMoreButtonEnabled { get => (bool)GetValue(ShowMoreButtonEnabledProperty); set => SetValue(ShowMoreButtonEnabledProperty, value); }
        public static readonly DependencyProperty ShowMoreButtonEnabledProperty = DependencyProperty.Register(
            nameof(ShowMoreButtonEnabled), typeof(bool), typeof(HexViewer),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnLayoutPropertyChanged));

        public bool ShowAscii { get => (bool)GetValue(ShowAsciiProperty); set => SetValue(ShowAsciiProperty, value); }
        public static readonly DependencyProperty ShowAsciiProperty = DependencyProperty.Register(
            nameof(ShowAscii), typeof(bool), typeof(HexViewer),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnLayoutPropertyChanged));

        public HexTextEncoding TextEncoding
        {
            get => (HexTextEncoding)GetValue(TextEncodingProperty);
            set => SetValue(TextEncodingProperty, value);
        }

        public static readonly DependencyProperty TextEncodingProperty = DependencyProperty.Register(
            nameof(TextEncoding), typeof(HexTextEncoding), typeof(HexViewer),
            new FrameworkPropertyMetadata(HexTextEncoding.Ascii, FrameworkPropertyMetadataOptions.AffectsRender,
                null, CoerceTextEncoding));

        public bool IsCompact { get => (bool)GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
            nameof(IsCompact), typeof(bool), typeof(HexViewer),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnLayoutPropertyChanged));

        public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
            nameof(FontSize), typeof(double), typeof(HexViewer), LayoutMetadata(15d, CoercePositiveDouble));

        private static FrameworkPropertyMetadata LayoutMetadata(object defaultValue, CoerceValueCallback coerce) =>
            new(defaultValue, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
                OnLayoutPropertyChanged, coerce);

        private static object CoercePositiveInt(DependencyObject _, object value) => Math.Max(1, (int)value);
        private static object CoercePositiveDouble(DependencyObject _, object value) =>
            double.IsFinite((double)value) ? Math.Max(1d, (double)value) : 1d;
        private static object CoerceNonNegativeDouble(DependencyObject _, object value) =>
            double.IsFinite((double)value) ? Math.Max(0d, (double)value) : 0d;
        private static object CoerceTextEncoding(DependencyObject _, object value) =>
            value is HexTextEncoding encoding && Enum.IsDefined(encoding) ? encoding : HexTextEncoding.Ascii;

        private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs _)
        {
            if (d is not HexViewer viewer || viewer._menu is null) return;
            viewer.ClearHoverState();
            int totalRows = viewer.CalculateTotalRows();
            viewer.FirstVisibleRowIndex = viewer.FirstVisibleRowIndex;
            viewer.TotalRowsChanged?.Invoke(viewer, totalRows);
        }
        public uint? HoveredValue { get; private set; }

        public event EventHandler<uint?>? HoveredTetradChanged;
        public event EventHandler<int>? FirstVisibleRowChanged;
        public event EventHandler<int>? TotalRowsChanged;
        public event EventHandler<IList<byte>>? DataChangedExternally;

        private void RaiseDataChangedExternally()
        {
            if (Data != null)
                DataChangedExternally?.Invoke(this, Data);
        }

        public double CalculatedOffsetWidth
        {
            get
            {
                EnsureTextCache();
                int rows = CalculateTotalRows();
                int maxOffset = Math.Max(0, (rows - 1) * DisplayColumns - VisualByteOffset);
                int digits = Math.Max(3, maxOffset.ToString("X").Length);

                string widestOffset = new string('F', digits);
                var sample = _texts.Create(widestOffset);

                return sample.Width + OffsetToLinePadding;
            }
        }

        public int FirstVisibleRowIndex
        {
            get => _firstVisibleRowIndex;
            set
            {
                int maxIndex = Math.Max(0, GetTotalRows() - Math.Max(1, GetRowsToDraw()));
                int newValue = Math.Max(0, Math.Min(maxIndex, value));
                if (_firstVisibleRowIndex != newValue)
                {
                    _firstVisibleRowIndex = newValue;
                    ClearHoverState();
                    FirstVisibleRowChanged?.Invoke(this, _firstVisibleRowIndex);
                    InvalidateVisual();
                }
            }
        }

        public bool IsEditable
        {
            get => (bool)GetValue(IsEditableProperty);
            set => SetValue(IsEditableProperty, value);
        }

        public static readonly DependencyProperty IsEditableProperty =
            DependencyProperty.Register(
                nameof(IsEditable),
                typeof(bool),
                typeof(HexViewer),
                new PropertyMetadata(true, OnIsEditableChanged));

        private static void OnIsEditableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not HexViewer v)
                return;

            v._menu?.SetEditable((bool)e.NewValue);
            v._menu?.SetLoadVisible(v.IsLoadFromBinVisible && v.IsEditable);
            v.InvalidateVisual();
        }

        public int VisualByteOffset
        {
            get => (int)GetValue(VisualByteOffsetProperty);
            set => SetValue(VisualByteOffsetProperty, value);
        }

        public static readonly DependencyProperty VisualByteOffsetProperty =
            DependencyProperty.Register(
                nameof(VisualByteOffset),
                typeof(int),
                typeof(HexViewer),
                new FrameworkPropertyMetadata(
                    0,
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnVisualByteOffsetChanged,
                    CoerceVisualByteOffset));

        public int HighlightedRangeStart
        {
            get => (int)GetValue(HighlightedRangeStartProperty);
            set => SetValue(HighlightedRangeStartProperty, value);
        }

        public static readonly DependencyProperty HighlightedRangeStartProperty =
            DependencyProperty.Register(
                nameof(HighlightedRangeStart),
                typeof(int),
                typeof(HexViewer),
                new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.AffectsRender,
                    null, (_, value) => Math.Max(-1, (int)value)));

        public int HighlightedRangeLength
        {
            get => (int)GetValue(HighlightedRangeLengthProperty);
            set => SetValue(HighlightedRangeLengthProperty, value);
        }

        public static readonly DependencyProperty HighlightedRangeLengthProperty =
            DependencyProperty.Register(
                nameof(HighlightedRangeLength),
                typeof(int),
                typeof(HexViewer),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender,
                    null, (_, value) => Math.Max(0, (int)value)));

        private static object CoerceVisualByteOffset(DependencyObject d, object baseValue) =>
            Math.Clamp((int)baseValue, 0, 3);

        private static void OnVisualByteOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not HexViewer viewer)
                return;

            bool refreshOpenTooltip = viewer._tooltip.IsOpen && viewer.IsMouseOver;
            viewer.ClearHoverState();

            int totalRows = viewer.CalculateTotalRows();
            viewer.FirstVisibleRowIndex = viewer.FirstVisibleRowIndex;

            viewer.TotalRowsChanged?.Invoke(viewer, totalRows);

            if (refreshOpenTooltip)
            {
                viewer.Dispatcher.BeginInvoke(() =>
                    viewer.UpdateHover(Mouse.GetPosition(viewer), showTooltipImmediately: true));
            }
        }


        #region WordDisplaySizeProperty

        public WordSizeEnum WordDisplaySize
        {
            get => (WordSizeEnum)GetValue(WordDisplaySizeProperty);
            set => SetValue(WordDisplaySizeProperty, value);
        }

        public static readonly DependencyProperty WordDisplaySizeProperty =
            DependencyProperty.Register(
                nameof(WordDisplaySize),
                typeof(WordSizeEnum),
                typeof(HexViewer),
                new FrameworkPropertyMetadata(
                    WordSizeEnum.Byte4,
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnWordSizeChanged));

        private static void OnWordSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HexViewer hv && e.NewValue is WordSizeEnum ws)
                hv.ApplyWordSize(ws);
        }

        private void ApplyWordSize(WordSizeEnum size)
        {
            switch (size)
            {
                case WordSizeEnum.Byte1:
                    SetCurrentValue(GroupSizeProperty, 1);
                    SetCurrentValue(GroupSpacingProperty, 1d);
                    break;
                case WordSizeEnum.Byte2:
                    SetCurrentValue(GroupSizeProperty, 2);
                    SetCurrentValue(GroupSpacingProperty, 4d);
                    break;
                case WordSizeEnum.Byte4:
                    SetCurrentValue(GroupSizeProperty, 4);
                    SetCurrentValue(GroupSpacingProperty, 8d);
                    break;
            }

            ClearHoverState();

            InvalidateMeasure();
            InvalidateVisual();
        }


        #endregion

        #region Data Property

        private IHexDataSource? _dataSource;

        public IList<byte> Data
        {
            get => (IList<byte>)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(
                nameof(Data),
                typeof(IList<byte>),
                typeof(HexViewer),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.AffectsRender |
                    FrameworkPropertyMetadataOptions.AffectsMeasure,
                    OnDataChanged));


        private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HexViewer viewer)
            {
                viewer.ClearHoverState();

                // обновляем источник один раз при изменении Data
                viewer._dataSource = viewer.Data is null ? null : new ListHexDataSource(viewer.Data);

                int totalRows = viewer.CalculateTotalRows();

                if (totalRows < viewer._prevTotalRows)
                    viewer.FirstVisibleRowIndex = 0;
                else
                    viewer.FirstVisibleRowIndex = viewer.FirstVisibleRowIndex;

                viewer._prevTotalRows = totalRows;

                viewer.InvalidateVisual();
            }
        }
        #endregion

        #region IsLoadVisibleProperty

        public bool IsLoadFromBinVisible
        {
            get => (bool)GetValue(IsLoadFromBinVisibleProperty);
            set => SetValue(IsLoadFromBinVisibleProperty, value);
        }

        public static readonly DependencyProperty IsLoadFromBinVisibleProperty =
            DependencyProperty.Register(
                nameof(IsLoadFromBinVisible),
                typeof(bool),
                typeof(HexViewer),
                new PropertyMetadata(true, OnIsLoadFromBinVisibleChanged));

        private static void OnIsLoadFromBinVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HexViewer v)
            {
                bool visible = (bool)e.NewValue;
                v._menu?.SetLoadVisible(visible && v.IsEditable);
            }
        }

        #endregion

        #region Mouse Moves

        protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseRightButtonUp(e);

            _tooltip.Hide(); // <= важно

            bool hasData = Data is { Count: > 0 };
            _geo.Rebuild(DisplayColumns, GroupSize, DisplayGroupSpacing, DisplayCellWidth, CellHeight,
                CompactLayout ? 0 : CalculatedOffsetWidth);
            int size = 0;
            int? startIndex = hasData && HitTestService.TryGetGroup(e.GetPosition(this), _geo,
                FirstVisibleRowIndex, GetRowsToDraw(), DisplayColumns, GroupSize, CellHeight,
                VisualByteOffset, Data!.Count, CompactLayout, out var start, out size) ? start : null;
            if (startIndex.HasValue) _hoveredGroupSize = size;

            _menu.OpenAtMouse(hasData, startIndex);

            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoveredGroupStartIndex != null || HoveredValue != null)
            {
                ClearHoverState();
                InvalidateVisual();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            Focus();

            var pt = e.GetPosition(this);

            if (_showMoreButtonRect != Rect.Empty && _showMoreButtonRect.Contains(pt))
            {
                OpenFullViewerWindow();
                e.Handled = true;
                return;
            }

            if (IsEditable && e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && Data is { Count: > 0 })
            {
                OpenEditorWindow();
                e.Handled = true;
                return;
            }

            base.OnMouseDown(e);
        }

        private void OpenFullViewerWindow()
        {
            if (Data == null)
                return;

            _tooltip.Hide();

            var wnd = new global::HexViewer.HexViewerWindow(Data)
            {
                DisplayMode = DisplayMode,
                Owner = Window.GetWindow(this),
                VisualByteOffset = VisualByteOffset,
                ShowAscii = ShowAscii,
                TextEncoding = TextEncoding,
                ByteOrder = ByteOrder,
                ByteDisplayOrder = ByteDisplayOrder,
                WordDisplaySize = WordDisplaySize
            };

            wnd.Show();

        }
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            int previousRow = FirstVisibleRowIndex;
            int step = e.Delta > 0 ? -1 : 1;
            FirstVisibleRowIndex += step;

            ClearHoverState();

            InvalidateVisual();
            e.Handled = FirstVisibleRowIndex != previousRow;
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Data == null) return;

            UpdateHover(e.GetPosition(this));
        }

        private void UpdateHover(Point pt, bool showTooltipImmediately = false)
        {
            if (WordLayout) { ClearHoverState(); return; }
            if (Data == null)
                return;

            // hover по "Показать ещё"
            if (ShowMoreButtonEnabled)
            {
                if (_showMoreButtonRect != Rect.Empty && _showMoreButtonRect.Contains(pt))
                {
                    if (!_showMoreButtonHovered) { _showMoreButtonHovered = true; InvalidateVisual(); }
                    if (_hoveredGroupStartIndex != null)
                    {
                        ClearHoverState();
                        InvalidateVisual();
                    }
                    return;
                }
                else if (_showMoreButtonHovered)
                {
                    _showMoreButtonHovered = false;
                    InvalidateVisual();
                }
            }

            // Mouse input can arrive before the first render or after layout properties change.
            _geo.Rebuild(DisplayColumns, GroupSize, DisplayGroupSpacing, DisplayCellWidth, CellHeight,
                IsCompact ? 0 : CalculatedOffsetWidth);

            // Попробуем найти тетраду
            if (HitTestService.TryGetGroup(
                    pt, _geo, FirstVisibleRowIndex, GetRowsToDraw(), DisplayColumns, GroupSize, CellHeight,
                    VisualByteOffset, Data.Count, IsCompact,
                    out int startIndex, out int groupSize) &&
                _math.IsTetradValid(Data, startIndex))
            {
                if (_hoveredGroupStartIndex != startIndex || _hoveredGroupSize != groupSize)
                {
                    _hoveredGroupStartIndex = startIndex;
                    _hoveredGroupSize = groupSize;
                    HoveredValue = TryParse(startIndex, groupSize);

                    HoveredTetradChanged?.Invoke(this, HoveredValue);

                    if (HoveredValue is uint v)
                    {
                        int displayColumn = (startIndex + VisualByteOffset) % DisplayColumns;
                        int groupColumn = displayColumn / GroupSize * GroupSize;
                        _tooltip.ShowLater(
                            new Point(_geo.ColumnX[groupColumn], 0),
                            v, Data,
                            startIndex, DisplayColumns, FirstVisibleRowIndex,
                            DisplayCellWidth, CellHeight, groupSize, WordDisplaySize, VisualByteOffset,
                            showTooltipImmediately);
                    }

                    InvalidateVisual();
                }
                return;
            }

            // сюда попадаем если ни одна тетрада не подошла
            if (_hoveredGroupStartIndex != null)
            {
                ClearHoverState();
                InvalidateVisual();
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

            if (IsEditable && (e.Key == Key.Enter || ctrl && e.Key == Key.E) && Data is { Count: > 0 })
            {
                OpenEditorWindow();
                e.Handled = true;
            }
        }

        protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
        {
            base.OnLostKeyboardFocus(e);
            _tooltip.Hide();
        }

        #endregion

        #region Cache Text

        private double _lastDpi;
        private int _lastOffsetDigits;
        private Typeface _lastTypeface = new Typeface("Consolas");
        private double _lastFontSize = 15;
        private Brush _lastBrush = Brushes.Black;

        private void EnsureTextCache()
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            int rows = CalculateTotalRows();
            int maxOffset = Math.Max(0, (rows - 1) * DisplayColumns - VisualByteOffset);
            int offsetDigits = Math.Max(3, maxOffset.ToString("X").Length);

            if (_lastDpi != dpi || _lastOffsetDigits != offsetDigits || _lastFontSize != FontSize ||
                !ReferenceEquals(_lastBrush, Foreground))
            {
                _lastDpi = dpi;
                _lastOffsetDigits = offsetDigits;
                _lastFontSize = FontSize;
                _lastBrush = Foreground;

                _texts.Rebuild(_lastTypeface, _lastFontSize, _lastBrush, dpi, offsetDigits);
            }
        }

        #endregion

        private void OpenEditorWindow()
        {
            if (!IsEditable)
                return;

            if (Data == null)
                SetCurrentValue(DataProperty, new List<byte>());

            _tooltip.Hide();

            var wnd = new global::HexViewer.HexEditorWindow(Data)
            {
                DisplayMode = DisplayMode,
                Owner = Window.GetWindow(this),
                ShowAscii = ShowAscii,
                TextEncoding = TextEncoding
            };
            wnd.Editor.ByteOrder = ByteOrder;
            wnd.ShowDialog();

            if (!wnd.IsSaved)
                return;

            SetCurrentValue(DataProperty, wnd.GetBytes());
            FirstVisibleRowIndex = 0;
            InvalidateMeasure();
            InvalidateVisual();
            RaiseDataChangedExternally();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            UpdateColumns(availableSize.Width);
            int totalRows = Math.Max(1, CalculateTotalRows());
            int rowsToDraw = AutoFitRowsToHeight && double.IsFinite(availableSize.Height)
                ? CalculateRowsForHeightCore(availableSize.Height, totalRows, CellHeight, ShowMoreButtonEnabled, CompactLayout)
                : Math.Max(1, Math.Min(VisibleRows, totalRows));

            double width = CalculateContentWidth(DisplayColumns);

            double height = AutoFitRowsToHeight && double.IsFinite(availableSize.Height)
                ? Math.Max(CellHeight * (CompactLayout ? 1 : 2), availableSize.Height)
                : CellHeight * (rowsToDraw + (CompactLayout ? 0 : 1));

            if (!AutoFitRowsToHeight && ShowMoreButtonEnabled && Data != null && rowsToDraw < totalRows)
                height += CellHeight + 10;

            return new Size(width, height);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (!AutoFitRowsToHeight && !AutoFitColumnsToWidth) return;
            UpdateColumns(sizeInfo.NewSize.Width);
            InvalidateMeasure();
            ClearHoverState();
            FirstVisibleRowIndex = FirstVisibleRowIndex;
            TotalRowsChanged?.Invoke(this, CalculateTotalRows());
        }

        public double CalculateContentWidth(int columns)
        {
            columns = Math.Max(1, columns);
            int groupCount = (columns - 1) / Math.Max(1, GroupSize);
            double offsetWidth = CompactLayout ? 0 : CalculatedOffsetWidth;
            double hexWidth = offsetWidth + columns * DisplayCellWidth + groupCount * DisplayGroupSpacing;
            return AsciiLayout ? hexWidth + AsciiGap + columns * CalculatedAsciiCellWidth : hexWidth;
        }

        public double CalculatedAsciiCellWidth
        {
            get
            {
                EnsureTextCache();
                return Math.Max(8 * FontSize / 15, _texts.Create("W").WidthIncludingTrailingWhitespace);
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Background ?? TransparentBrush, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

            // Пересобрать геометрию/кэш текста
            EnsureTextCache();
            _geo.Rebuild(DisplayColumns, GroupSize, DisplayGroupSpacing, DisplayCellWidth, CellHeight,
                CompactLayout ? 0 : CalculatedOffsetWidth);

            int totalRows = Math.Max(1, GetTotalRows());
            int rowsToDraw = Math.Max(1, GetRowsToDraw());

            var ctx = new RenderContext(
                DisplayColumns, GroupSize, DisplayCellWidth, CellHeight, DisplayGroupSpacing,
                FirstVisibleRowIndex, rowsToDraw,
                _hoveredGroupStartIndex,
                _geo, _texts,
                _dataSource,
                _lastOffsetDigits,
                VisualByteOffset,
                HighlightedRangeStart,
                HighlightedRangeLength,
                CompactLayout,
                AsciiLayout,
                TextEncoding,
                CalculateContentWidth(DisplayColumns) - DisplayColumns * CalculatedAsciiCellWidth,
                CalculatedAsciiCellWidth, ByteDisplayOrder, DisplayMode);

            _hexRenderer.RenderHeader(dc, ctx);
            _hexRenderer.RenderGridLines(dc, ctx);
            _hexRenderer.RenderRows(dc, ctx);

            if (ShowMoreButtonEnabled && Data != null && rowsToDraw < totalRows)
            {
                _hexRenderer.RenderShowMore(dc, ctx, _showMoreButtonHovered, Data.Count, out _showMoreButtonRect);
            }
            else
            {
                _showMoreButtonRect = Rect.Empty;
                _showMoreButtonHovered = false;
            }
        }

        /// <summary>
        /// Общее количество строк по текущим данным и количеству столбцов.
        /// Метод не изменяет состояние и не поднимает события.
        /// </summary>
        public int GetTotalRows()
        {
            return CalculateTotalRows();
        }

        private int CalculateTotalRows()
        {
            int count = Data?.Count ?? 0;
            int cols = Math.Max(1, DisplayColumns);
            return count == 0 ? 0 : (int)Math.Ceiling((count + VisualByteOffset) / (double)cols);
        }

        /// <summary>
        /// Сколько строк реально рисовать (ограничено VisibleRows).
        /// </summary>
        private int GetRowsToDraw()
        {
            int totalRows = GetTotalRows();
            if (!AutoFitRowsToHeight || RenderSize.Height <= 0)
                return Math.Min(VisibleRows, totalRows);

            return CalculateRowsForHeightCore(RenderSize.Height, totalRows, CellHeight, ShowMoreButtonEnabled, CompactLayout);
        }

        private static int CalculateRowsForHeight(double availableHeight, int totalRows, double cellHeight,
            bool reserveShowMore)
        {
            return CalculateRowsForHeightCore(availableHeight, totalRows, cellHeight, reserveShowMore, false);
        }

        private static int CalculateRowsForHeightCore(double availableHeight, int totalRows, double cellHeight,
            bool reserveShowMore, bool isCompact)
        {
            if (totalRows <= 0)
                return 0;

            int headerRows = isCompact ? 0 : 1;
            int rowsWithoutFooter = Math.Max(1, (int)Math.Floor(availableHeight / cellHeight) - headerRows);
            if (!reserveShowMore || rowsWithoutFooter >= totalRows)
                return Math.Min(totalRows, rowsWithoutFooter);

            int rowsWithFooter = Math.Max(1,
                (int)Math.Floor((availableHeight - 10) / cellHeight) - headerRows - 1);
            return Math.Min(totalRows, rowsWithFooter);
        }

        private uint? TryParse(int startIndex, int groupSize)
        {
            return Data == null ? null : HexWordOrder.Read(Data, startIndex, groupSize, ByteOrder);
        }

        private void ClearHoverState()
        {
            bool changed = _hoveredGroupStartIndex != null || HoveredValue != null;
            _hoveredGroupStartIndex = null;
            _hoveredGroupSize = 0;
            HoveredValue = null;
            _tooltip?.Hide();
            if (changed) HoveredTetradChanged?.Invoke(this, null);
        }
    }
}
