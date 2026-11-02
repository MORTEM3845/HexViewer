using MessageBox = System.Windows.MessageBox;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using HexViewer.Items;

using System.Windows.Controls;
using HexViewer.Items.Enums;

namespace HexViewer
{
    public partial class HexViewerWindow : Window
    {
        private const double WindowChromeWidth = 60;

        public static readonly DependencyProperty VisibleRowsProperty =
       DependencyProperty.Register(
           nameof(VisibleRows),
           typeof(int),
           typeof(HexViewerWindow),
           new FrameworkPropertyMetadata(
               8,
               FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
               OnVisibleRowsChanged, (_, value) => Math.Max(1, (int)value)));

        private static void OnVisibleRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is HexViewerWindow window && window.HexViewerControl != null)
                window.HexViewerControl.VisibleRows = (int)e.NewValue;
        }

        public int VisibleRows
        {
            get => (int)GetValue(VisibleRowsProperty);
            set => SetValue(VisibleRowsProperty, value);
        }

        private List<int> _searchMatches = new();
        private int _currentMatchIndex = -1;
        private int _searchPatternLength;
        private CancellationTokenSource? _searchCancellation;
        private SearchWindow? _searchWindow;

        private IList<byte> _data;

        public HexDisplayMode DisplayMode
        {
            get => HexViewerControl.DisplayMode;
            set => HexViewerControl.DisplayMode = value;
        }

        public int VisualByteOffset
        {
            get => HexViewerControl.VisualByteOffset;
            set
            {
                int offset = Math.Clamp(value, 0, 3);
                HexViewerControl.VisualByteOffset = offset;
                if (VisualOffsetBox.SelectedIndex != offset)
                    VisualOffsetBox.SelectedIndex = offset;
            }
        }

        public bool ShowAscii
        {
            get => HexViewerControl.ShowAscii;
            set
            {
                HexViewerControl.ShowAscii = value;
                if (ShowAsciiCheckBox.IsChecked != value)
                    ShowAsciiCheckBox.IsChecked = value;
                if (MainGrid.ActualWidth > 0)
                    UpdateColumnsForWidth();
            }
        }

        public HexTextEncoding TextEncoding
        {
            get => HexViewerControl.TextEncoding;
            set
            {
                HexViewerControl.TextEncoding = value;
                TextEncodingBox.SelectedIndex = EncodingToSelectedIndex(value);
            }
        }

        public HexByteOrder ByteOrder
        {
            get => HexViewerControl.ByteOrder;
            set => HexViewerControl.ByteOrder = value;
        }

        public HexByteDisplayOrder ByteDisplayOrder
        {
            get => HexViewerControl.ByteDisplayOrder;
            set => HexViewerControl.ByteDisplayOrder = value;
        }

        public WordSizeEnum WordDisplaySize
        {
            get => HexViewerControl.WordDisplaySize;
            set => HexViewerControl.WordDisplaySize = value;
        }

        public HexViewerWindow(IList<byte> data)
        {
            InitializeComponent();
            HexViewerControl.AddWordSizeShortcuts(this);
            Focusable = true;

            _data = data;

            //title
            this.Title = $"Просмотр HEX — {_data.Count.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} Б";

            HexViewerControl.Data = data;
            HexViewerControl.Columns = 32;
            HexViewerControl.VisibleRows = VisibleRows;

            int totalRows = HexViewerControl.GetTotalRows();
            int maxIndex = Math.Max(0, totalRows - VisibleRows);

            //не давать подгружать данные
            HexViewerControl.IsLoadFromBinVisible = false;

            //кнопки показать еще - нет
            HexViewerControl.ShowMoreButtonEnabled = false;

            MinWidth = CalculateViewerWidth(8, HexViewerControl.CalculatedOffsetWidth,
                HexViewerControl.CellWidth, HexViewerControl.GroupSize, HexViewerControl.GroupSpacing)
                + WindowChromeWidth;
            Width = CalculateViewerWidth(32, HexViewerControl.CalculatedOffsetWidth,
                HexViewerControl.CellWidth, HexViewerControl.GroupSize, HexViewerControl.GroupSpacing)
                + WindowChromeWidth;

            VerticalScrollBar.Minimum = 0;
            VerticalScrollBar.Maximum = maxIndex;
            VerticalScrollBar.LargeChange = VisibleRows;
            VerticalScrollBar.SmallChange = 1;
            VerticalScrollBar.ViewportSize = VisibleRows;

            VerticalScrollBar.ValueChanged += (s, e) =>
            {
                HexViewerControl.FirstVisibleRowIndex = (int)VerticalScrollBar.Value;
            };

            HexViewerControl.FirstVisibleRowChanged += (s, newIndex) =>
            {
                if ((int)VerticalScrollBar.Value != newIndex)
                    VerticalScrollBar.Value = newIndex;
            };

            HexViewerControl.TotalRowsChanged += (s, rows) =>
            {
                int maxIndex2 = Math.Max(0, rows - HexViewerControl.VisibleRows);
                VerticalScrollBar.Maximum = maxIndex2;
                VerticalScrollBar.LargeChange = HexViewerControl.VisibleRows;
                VerticalScrollBar.ViewportSize = HexViewerControl.VisibleRows;
            };

            this.SizeChanged += (s, e) =>
            {
                UpdateColumnsForWidth();

                double gridHeight = MainGrid.ActualHeight;
                int headerHeight = (int)HexViewerControl.CellHeight;
                int cellHeight = (int)HexViewerControl.CellHeight;

                // Считаем сколько реально строк помещается на экране:
                int newVisibleRows = Math.Max(1, (int)((gridHeight - headerHeight - 2 * 10) / cellHeight));

                if (HexViewerControl.VisibleRows != newVisibleRows)
                {
                    VisibleRows = newVisibleRows;

                    int maxScroll = Math.Max(0, HexViewerControl.GetTotalRows() - newVisibleRows);
                    VerticalScrollBar.Maximum = maxScroll;
                    VerticalScrollBar.ViewportSize = newVisibleRows;

                    if (VerticalScrollBar.Value > maxScroll)
                        VerticalScrollBar.Value = maxScroll;
                    if (HexViewerControl.FirstVisibleRowIndex > maxScroll)
                        HexViewerControl.FirstVisibleRowIndex = maxScroll;
                }
            };
        }

        private void UpdateColumnsForWidth()
        {
            double scrollBarWidth = Math.Max(VerticalScrollBar.ActualWidth, SystemParameters.VerticalScrollBarWidth);
            double availableViewerWidth = MainGrid.ActualWidth
                - scrollBarWidth
                - VerticalScrollBar.Margin.Left - VerticalScrollBar.Margin.Right
                - HexViewerControl.Margin.Left - HexViewerControl.Margin.Right;
            int columns = GetAdaptiveColumnCountForDisplay(availableViewerWidth);

            if (HexViewerControl.Columns == columns)
                return;

            int firstVisibleDisplayIndex = HexViewerControl.FirstVisibleRowIndex * HexViewerControl.Columns;
            HexViewerControl.Columns = columns;
            int firstVisibleRow = firstVisibleDisplayIndex / columns;
            HexViewerControl.FirstVisibleRowIndex = firstVisibleRow;
            VerticalScrollBar.Value = firstVisibleRow;
        }

        private int GetAdaptiveColumnCountForDisplay(double availableWidth) =>
            HexViewerControl.CalculateFittedColumns(availableWidth);

        private static double CalculateViewerWidth(int columns, double offsetWidth,
            double cellWidth, int groupSize, double groupSpacing) =>
            offsetWidth + columns * cellWidth + ((columns - 1) / groupSize) * groupSpacing;

        private void GoToAddress_Click(object sender, RoutedEventArgs e)
        {
            ClearSearchHighlight();

            if (HexParser.TryParseAddress(AddressBox.Text, out int addr))
            {
                int row = (addr + VisualByteOffset) / HexViewerControl.Columns;
                HexViewerControl.FirstVisibleRowIndex = row;
                VerticalScrollBar.Value = row;
            }
            else
            {
                MessageBox.Show("Неверный формат адреса");
            }
        }

        private void FindCommand_Executed(object sender, ExecutedRoutedEventArgs e) => OpenSearchWindow();

        private void OpenSearch_Click(object sender, RoutedEventArgs e) => OpenSearchWindow();

        private void OpenSearchWindow()
        {
            if (_searchWindow is { IsVisible: true })
            {
                _searchWindow.Activate();
                _searchWindow.FocusSearchText();
                return;
            }

            ClearSearch();
            var searchWindow = new SearchWindow { Owner = this };
            _searchWindow = searchWindow;
            searchWindow.SearchChanged += (_, _) => ClearSearch();
            searchWindow.FindRequested += FindBytes;
            searchWindow.NextRequested += (_, _) => ShowNextMatch();
            searchWindow.PreviousRequested += (_, _) => ShowPreviousMatch();
            searchWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_searchWindow, searchWindow))
                    _searchWindow = null;
            };
            searchWindow.Show();
        }

        private async void FindBytes(object? sender, EventArgs e)
        {
            _searchCancellation?.Cancel();
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            _searchPatternLength = 0;
            ClearSearchHighlight();

            if (_searchWindow == null || !_searchWindow.TryGetPattern(out byte[] pattern))
                return;

            int startIndex = _searchWindow.SearchFromCurrentAddress
                ? (int)Math.Clamp((long)HexViewerControl.FirstVisibleRowIndex * HexViewerControl.Columns -
                    VisualByteOffset, 0, _data.Count)
                : 0;

            _searchCancellation = new CancellationTokenSource();
            CancellationToken searchToken = _searchCancellation.Token;
            try
            {
                _searchWindow.Status = "Поиск";
                IReadOnlyList<byte> snapshot = _data as IReadOnlyList<byte> ?? _data.ToArray();
                var matches = await Task.Run(() => ByteSearch.FindMatchesFrom(snapshot, pattern, startIndex,
                    ByteSearch.DefaultMatchLimit, searchToken));
                searchToken.ThrowIfCancellationRequested();
                _searchMatches.AddRange(matches);
                _searchPatternLength = pattern.Length;
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (_searchWindow == null)
                return;

            _searchWindow.Status = _searchMatches.Count switch
            {
                0 => "Ничего не найдено",
                ByteSearch.DefaultMatchLimit => $"Найдено: не менее {_searchMatches.Count:N0} (достигнут лимит)",
                _ => $"Найдено: {_searchMatches.Count}"
            };
            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                ScrollToMatch();
                UpdateSearchPosition();
            }
        }

        private void ScrollToMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;

            int byteIndex = _searchMatches[_currentMatchIndex];
            int row = (byteIndex + VisualByteOffset) / HexViewerControl.Columns;

            HexViewerControl.HighlightedRangeStart = byteIndex;
            HexViewerControl.HighlightedRangeLength = _searchPatternLength;

            HexViewerControl.FirstVisibleRowIndex = row;
            VerticalScrollBar.Value = row;
        }

        private void ShowNextMatch()
        {
            if (_searchMatches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
            ScrollToMatch();
            UpdateSearchPosition();
        }

        private void ShowPreviousMatch()
        {
            if (_searchMatches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            ScrollToMatch();
            UpdateSearchPosition();
        }

        private void VisualOffsetBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!IsInitialized || VisualOffsetBox.SelectedIndex < 0)
                return;

            VisualByteOffset = VisualOffsetBox.SelectedIndex;
        }

        private void ShowAsciiCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (HexViewerControl == null)
                return;

            HexViewerControl.ShowAscii = ShowAsciiCheckBox.IsChecked == true;
            if (MainGrid?.ActualWidth > 0)
                UpdateColumnsForWidth();
        }

        private void TextEncodingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HexViewerControl == null || TextEncodingBox.SelectedItem is not ComboBoxItem item ||
                !Enum.TryParse(item.Tag?.ToString(), out HexTextEncoding encoding))
                return;

            HexViewerControl.TextEncoding = encoding;
        }

        private static int EncodingToSelectedIndex(HexTextEncoding encoding) => encoding switch
        {
            HexTextEncoding.Utf8 => 1,
            HexTextEncoding.Windows1251 => 2,
            _ => 0
        };

        private void ClearSearch()
        {
            _searchCancellation?.Cancel();
            _searchMatches.Clear();
            _currentMatchIndex = -1;
            _searchPatternLength = 0;
            if (_searchWindow != null)
                _searchWindow.Status = string.Empty;
            ClearSearchHighlight();
        }

        private void UpdateSearchPosition()
        {
            if (_searchWindow != null && _currentMatchIndex >= 0)
                _searchWindow.Status = $"Найдено: {_searchMatches.Count}, текущий: {_currentMatchIndex + 1}";
        }

        private void ClearSearchHighlight()
        {
            HexViewerControl.HighlightedRangeStart = -1;
            HexViewerControl.HighlightedRangeLength = 0;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                e.Key is Key.Left or Key.Right)
            {
                ChangeVisualOffset(e.Key == Key.Right ? 1 : -1);
                e.Handled = true;
                return;
            }

            base.OnPreviewKeyDown(e);
        }

        protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                ChangeVisualOffset(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
                return;
            }

            base.OnPreviewMouseWheel(e);
        }

        private void ChangeVisualOffset(int delta) =>
            VisualByteOffset = GetCycledVisualOffset(VisualByteOffset, delta);

        private static int GetCycledVisualOffset(int current, int delta) =>
            (current + delta % 4 + 4) % 4;
    }
}
