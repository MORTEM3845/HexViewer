using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using HexViewer.Control.Services;
using HexViewer.Enums;

namespace HexViewer.Control.HexViewer
{
    public partial class HexViewer : FrameworkElement
    {
        private const double OffsetToLinePadding = 8;

        private int _firstVisibleRowIndex;
        private int? _contextTetradStartIndex;
        private int? _hoveredGroupStartIndex;
        private int _prevTotalRows;
        private int _defaultVisibleRows = 8;

        private bool _showMoreButtonHovered;
        private Rect _showMoreButtonRect;

        private int _offsetDigits = 3;

        private readonly ViewportController _viewport = new();
        private readonly GeometryCache _geo = new();
        private readonly TextCache _texts = new();
        private readonly TooltipController _tooltip;
        private readonly ContextMenuController _menu;
        private readonly DefaultHexRenderer _hexRenderer = new DefaultHexRenderer();

        private MathHelper _math = new();

        private static readonly Brush TransparentBrush = Brushes.Transparent;

        public HexViewer()
        {
            _tooltip = new TooltipController(this);

            // контекстное меню через контроллер
            _menu = new ContextMenuController(this);

            _menu.Bind(
                getData: () => _dataSource,
                getContextTetradStartIndex: () => _contextTetradStartIndex,
                getHoveredGroupStartIndex: () => _hoveredGroupStartIndex,
                getGroupSize: () => GroupSize,
                setData: bytes =>
                {
                    Data = bytes;
                    FirstVisibleRowIndex = 0;
                    InvalidateMeasure();
                    InvalidateVisual();
                    RaiseDataChangedExternally();
                },
                setWordSize: size => WordDisplaySize = size,
                getWordSize: () => WordDisplaySize
            );

            _menu.Attach(IsLoadFromBinVisible);
        }

        public int Columns { get; set; } = 32;
        public double CellWidth { get; set; } = 28;
        public double CellHeight { get; set; } = 20;

        public int GroupSize { get; set; } = 4;
        public double GroupSpacing { get; set; } = 8;
        public int VisibleRows { get; set; } = 8;
        public int RowsStep { get; set; } = 8;

        public bool ShowMoreButtonEnabled { get; set; } = true;
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
                int rows = (int)Math.Ceiling((Data?.Count ?? 0) / (double)Columns);
                int maxOffset = Math.Max(0, (rows - 1) * Columns);
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
                int maxIndex = Math.Max(0, GetTotalRows() - VisibleRows);
                int newValue = Math.Max(0, Math.Min(maxIndex, value));
                if (_firstVisibleRowIndex != newValue)
                {
                    _firstVisibleRowIndex = newValue;
                    FirstVisibleRowChanged?.Invoke(this, _firstVisibleRowIndex);
                    InvalidateVisual();
                }
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
                    GroupSize = 1;
                    GroupSpacing = 1;
                    break;
                case WordSizeEnum.Byte2:
                    GroupSize = 2;
                    GroupSpacing = 4;
                    break;
                case WordSizeEnum.Byte4:
                    GroupSize = 4;
                    GroupSpacing = 8;
                    break;
            }

            _hoveredGroupStartIndex = null;
            HoveredValue = null;

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
                viewer._hoveredGroupStartIndex = null;

                // обновляем источник один раз при изменении Data
                viewer._dataSource = viewer.Data is null ? null : new ListHexDataSource(viewer.Data);

                int totalRows = 0;
                if (viewer.Data != null && viewer.Data.Count > 0)
                    totalRows = (int)Math.Ceiling(viewer.Data.Count / (double)viewer.Columns);

                if (viewer.VisibleRows > totalRows)
                    viewer.VisibleRows = Math.Max(viewer._defaultVisibleRows, totalRows);

                if (totalRows < viewer._prevTotalRows)
                    viewer.FirstVisibleRowIndex = 0;
                else if (viewer.FirstVisibleRowIndex > totalRows - viewer.VisibleRows)
                    viewer.FirstVisibleRowIndex = Math.Max(0, totalRows - viewer.VisibleRows);

                viewer._prevTotalRows = totalRows;

                viewer.InvalidateVisual();
            }
        }
        //private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        //{
        //    if (d is HexViewer viewer)
        //    {
        //        viewer._hoveredGroupStartIndex = null;

        //        int totalRows = 0;
        //        if (viewer.Data != null && viewer.Data.Count > 0)
        //            totalRows = (int)Math.Ceiling(viewer.Data.Count / (double)viewer.Columns);

        //        if (viewer.VisibleRows > totalRows)
        //            viewer.VisibleRows = Math.Max(viewer._defaultVisibleRows, totalRows);

        //        if (totalRows < viewer._prevTotalRows)
        //            viewer.FirstVisibleRowIndex = 0;
        //        else if (viewer.FirstVisibleRowIndex > totalRows - viewer.VisibleRows)
        //            viewer.FirstVisibleRowIndex = Math.Max(0, totalRows - viewer.VisibleRows);

        //        viewer._prevTotalRows = totalRows;

        //        viewer.InvalidateVisual();
        //    }
        //}

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
                v._menu?.SetLoadVisible(visible);
            }
        }

        #endregion

        #region Mouse Moves

        protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseRightButtonUp(e);

            _tooltip.Hide(); // <= важно

            bool hasData = Data is { Count: > 0 };
            int? startIndex = _contextTetradStartIndex ?? _hoveredGroupStartIndex;

            if (startIndex is int s && !_math.IsTetradValid(Data, s))
                startIndex = null;

            _menu.OpenAtMouse(hasData, startIndex);

            e.Handled = true;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);

            if (_hoveredGroupStartIndex != null || HoveredValue != null)
            {
                _hoveredGroupStartIndex = null;
                HoveredValue = null;
                InvalidateVisual();
            }
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            var pt = e.GetPosition(this);
            if (_showMoreButtonRect != Rect.Empty && _showMoreButtonRect.Contains(pt))
            {
                _tooltip.Hide();

                var wnd = new HexViewerWindow(Data) { Owner = Window.GetWindow(this) };
                wnd.Show();

                e.Handled = true;
                return;
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            int step = e.Delta > 0 ? -1 : 1;
            FirstVisibleRowIndex += step;

            _hoveredGroupStartIndex = null;
            HoveredValue = null;
            _tooltip.Hide();

            InvalidateVisual();
            e.Handled = true;
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Data == null) return;

            var pt = e.GetPosition(this);

            // hover по "Показать ещё"
            if (ShowMoreButtonEnabled)
            {
                if (_showMoreButtonRect != Rect.Empty && _showMoreButtonRect.Contains(pt))
                {
                    if (!_showMoreButtonHovered) { _showMoreButtonHovered = true; InvalidateVisual(); }
                    return;
                }
                else if (_showMoreButtonHovered)
                {
                    _showMoreButtonHovered = false;
                    InvalidateVisual();
                }
            }

            // Попробуем найти тетраду
            if (HitTestService.TryGetGroupStartIndex(
                    pt, _geo, FirstVisibleRowIndex, Columns, GroupSize, CellHeight, out int startIndex)
                && _math.IsTetradValid(Data, startIndex))
            {
                if (_hoveredGroupStartIndex != startIndex)
                {
                    _hoveredGroupStartIndex = startIndex;
                    HoveredValue = TryParse(startIndex);

                    HoveredTetradChanged?.Invoke(this, HoveredValue);

                    if (HoveredValue is uint v)
                    {
                        _tooltip.ShowLater(
                            new Point(_geo.ColumnX[startIndex % Columns], 0),
                            v,
                            startIndex, Columns, FirstVisibleRowIndex,
                            CellWidth, CellHeight, GroupSize, WordDisplaySize);
                    }

                    InvalidateVisual();
                }
                return;
            }

            // сюда попадаем если ни одна тетрада не подошла
            if (_hoveredGroupStartIndex != null)
            {
                _hoveredGroupStartIndex = null;
                HoveredValue = null;
                HoveredTetradChanged?.Invoke(this, null);

                _tooltip.Hide();
                InvalidateVisual();
            }
        }


        //protected override void OnMouseMove(MouseEventArgs e)
        //{
        //    if (Data == null) return;

        //    var pt = e.GetPosition(this);

        //    if (!new Rect(new Point(0, 0), RenderSize).Contains(pt))
        //    {
        //        if (_hoveredGroupStartIndex != null)
        //        {
        //            _selection.ClearHover();
        //            _hoveredGroupStartIndex = null;
        //            HoveredValue = null;
        //            _hoveredRect = Rect.Empty;
        //            _tooltip.Hide();
        //            InvalidateVisual();
        //        }
        //        return;
        //    }

        //    // hover по "Показать ещё"
        //    if (ShowMoreButtonEnabled)
        //    {
        //        if (_showMoreButtonRect != Rect.Empty && _showMoreButtonRect.Contains(pt))
        //        {
        //            if (!_showMoreButtonHovered) { _showMoreButtonHovered = true; InvalidateVisual(); }
        //            return;
        //        }
        //        else if (_showMoreButtonHovered)
        //        {
        //            _showMoreButtonHovered = false;
        //            InvalidateVisual();
        //        }
        //    }

        //    //_hoveredRect кэш последнего выделения, если остался таким же не обновляем выходим
        //    if (_hoveredGroupStartIndex is int h && _hoveredRect.Contains(pt))
        //        return;

        //    // 1) Ниже последней видимой строки — ничего не ховерим
        //    int screenRow = HitTestService.GetRowIndexFromPoint(pt, CellHeight);
        //    int rowsToDraw = GetRowsToDraw();
        //    if (screenRow < 0 || screenRow >= rowsToDraw)
        //    {
        //        if (_selection.ClearHover())
        //        {
        //            _hoveredGroupStartIndex = null;
        //            HoveredValue = null;
        //            _tooltip.Hide();
        //            InvalidateVisual();
        //        }
        //        return;
        //    }

        //    // 2) Хит-тест группы + проверка, что тетраду вообще можно читать
        //    if (HitTestService.TryGetGroupStartIndex(pt, _geo, FirstVisibleRowIndex, Columns, GroupSize, CellHeight, out int start) && _math.IsTetradValid(Data, start))
        //    {
        //        if (_hoveredGroupStartIndex != start)
        //        {
        //            _hoveredGroupStartIndex = start;
        //            _selection.SetHover(start, TryParse);
        //            HoveredValue = _selection.HoveredValue;

        //            // Сохраняем прямоугольник текущей тетрады для будущих проверок
        //            int colStart = (start % Columns);
        //            int row = (start / Columns) - FirstVisibleRowIndex;
        //            double y = CellHeight + row * CellHeight;
        //            _hoveredRect = new Rect(_geo.ColumnX[colStart], y, CellWidth * GroupSize, CellHeight);

        //            if (HoveredValue is uint v)
        //            {
        //                _tooltip.ShowLater(
        //                    new Point(_geo.ColumnX[start % Columns], 0),
        //                    v,
        //                    start, Columns, FirstVisibleRowIndex,
        //                    CellWidth, CellHeight, GroupSize, WordDisplaySize);
        //            }
        //            InvalidateVisual();
        //        }
        //        return;
        //    }

        //    //// 3) Ничего не найдено — чистим подсветку и тултип
        //    //if (_selection.ClearHover())
        //    //{
        //    //    _hoveredGroupStartIndex = null;
        //    //    HoveredValue = null;
        //    //    _hoveredRect = Rect.Empty;
        //    //    _tooltip.Hide();
        //    //    InvalidateVisual();
        //    //}
        //    if (_hoveredGroupStartIndex != null)
        //    {
        //        _selection.ClearHover();
        //        _hoveredGroupStartIndex = null;
        //        HoveredValue = null;
        //        _hoveredRect = Rect.Empty;
        //        _tooltip.Hide();
        //        InvalidateVisual();
        //    }
        //}

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
            int rows = Data == null ? 0 : (int)Math.Ceiling(Data.Count / (double)Columns);
            int maxOffset = Math.Max(0, (rows - 1) * Columns);
            int offsetDigits = Math.Max(3, maxOffset.ToString("X").Length);

            if (_lastDpi != dpi || _lastOffsetDigits != offsetDigits)
            {
                _lastDpi = dpi;
                _lastOffsetDigits = offsetDigits;

                _texts.Rebuild(_lastTypeface, _lastFontSize, _lastBrush, dpi, offsetDigits);
            }
        }

        #endregion

        protected override Size MeasureOverride(Size availableSize)
        {
            int totalRows = Math.Max(1, Data == null ? 0 : (int)Math.Ceiling(Data.Count / (double)Columns));
            int rowsToDraw = Math.Max(1, Math.Min(VisibleRows, totalRows));

            int groupCount = (Columns - 1) / GroupSize;
            double width = CalculatedOffsetWidth + Columns * CellWidth + groupCount * GroupSpacing;

            // +1 строка — header, всегда одна строка данных
            double height = CellHeight * (rowsToDraw + 1);

            if (Data != null && VisibleRows < totalRows)
                height += CellHeight + 10;

            return new Size(width, height);
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(TransparentBrush, null, new Rect(0, 0, RenderSize.Width, RenderSize.Height));

            // Пересобрать геометрию/кэш текста
            EnsureTextCache();
            _geo.Rebuild(Columns, GroupSize, GroupSpacing, CellWidth, CellHeight, CalculatedOffsetWidth);

            int totalRows = Math.Max(1, GetTotalRows());
            int rowsToDraw = Math.Max(1, GetRowsToDraw());

            var ctx = new RenderContext(
                Columns, GroupSize, CellWidth, CellHeight, GroupSpacing,
                FirstVisibleRowIndex, rowsToDraw,
                _hoveredGroupStartIndex,
                _geo, _texts,
                _dataSource,
                _lastOffsetDigits);

            _hexRenderer.RenderHeader(dc, ctx);
            _hexRenderer.RenderGridLines(dc, ctx);
            _hexRenderer.RenderRows(dc, ctx);

            if (ShowMoreButtonEnabled && Data != null && VisibleRows < totalRows)
            {
                _hexRenderer.RenderShowMore(dc, ctx, _showMoreButtonHovered, _dataSource.Count, out _showMoreButtonRect);
            }
            else
            {
                _showMoreButtonRect = Rect.Empty;
                _showMoreButtonHovered = false;
            }
        }

        /// <summary>
        /// Общее количество строк по текущим данным и количеству столбцов.
        /// Также поднимает событие TotalRowsChanged.
        /// </summary>
        public int GetTotalRows()
        {
            int count = Data?.Count ?? 0;
            int cols = Math.Max(1, Columns);
            int rows = count == 0 ? 0 : (int)Math.Ceiling(count / (double)cols);

            TotalRowsChanged?.Invoke(this, rows);
            return rows;
        }

        /// <summary>
        /// Сколько строк реально рисовать (ограничено VisibleRows).
        /// </summary>
        private int GetRowsToDraw() => Math.Min(VisibleRows, GetTotalRows());

        private uint? TryParse(int startIndex)
        {
            if (Data == null) return null;

            int available = Math.Min(GroupSize, Data.Count - startIndex);
            if (available <= 0) return null;

            Span<byte> b = stackalloc byte[4]; // всегда 4 байта под uint
            for (int i = 0; i < available; i++) b[i] = Data[startIndex + i];

            return GroupSize switch
            {
                1 => b[0],
                2 => BitConverter.ToUInt16(b),
                4 => BitConverter.ToUInt32(b),
                _ => null
            };
        }
    }
}

