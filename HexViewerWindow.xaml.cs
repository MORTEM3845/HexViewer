using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;

namespace HexViewer
{
    public partial class HexViewerWindow : Window
    {
        public static readonly DependencyProperty VisibleRowsProperty =
       DependencyProperty.Register(
           nameof(VisibleRows),
           typeof(int),
           typeof(Control.HexViewer.HexViewer),
           new FrameworkPropertyMetadata(
               8,
               FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int VisibleRows
        {
            get => (int)GetValue(VisibleRowsProperty);
            set => SetValue(VisibleRowsProperty, value);
        }

        private List<int> _searchMatches = new();
        private int _currentMatchIndex = -1;

        private IList<byte> _data;

        public HexViewerWindow(IList<byte> data)
        {
            InitializeComponent();
            Focusable = true;

            _data = data;

            //title
            this.Title = $"Просмотр Hex — {_data.Count.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} Б";

            int totalRows = (int)Math.Ceiling((data?.Count ?? 0) / 32.0);
            int maxIndex = Math.Max(0, totalRows - VisibleRows);

            //не давать подгружать данные
            HexViewerControl.IsLoadFromBinVisible = false;

            //кнопки показать еще - нет
            HexViewerControl.ShowMoreButtonEnabled = false;

            HexViewerControl.Data = data;
            HexViewerControl.Columns = 32;
            HexViewerControl.VisibleRows = VisibleRows;

            double minWindowWidth = HexViewerControl.CalculatedOffsetWidth
                + HexViewerControl.Columns * HexViewerControl.CellWidth
                + ((HexViewerControl.Columns - 1) / HexViewerControl.GroupSize) * HexViewerControl.GroupSpacing
                + 60; // запас на скроллбар и рамку

            this.Width = minWindowWidth;

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
                VerticalScrollBar.ViewportSize = HexViewerControl.VisibleRows;
            };

            this.SizeChanged += (s, e) =>
            {
                double gridHeight = MainGrid.ActualHeight;
                int headerHeight = (int)HexViewerControl.CellHeight;
                int cellHeight = (int)HexViewerControl.CellHeight;

                // Считаем сколько реально строк помещается на экране:
                int newVisibleRows = Math.Max(1, (int)((gridHeight - headerHeight - 2 * 10) / cellHeight));

                if (HexViewerControl.VisibleRows != newVisibleRows)
                {
                    HexViewerControl.VisibleRows = newVisibleRows;

                    int maxScroll = Math.Max(0, totalRows - newVisibleRows);
                    VerticalScrollBar.Maximum = maxScroll;
                    VerticalScrollBar.ViewportSize = newVisibleRows;

                    if (VerticalScrollBar.Value > maxScroll)
                        VerticalScrollBar.Value = maxScroll;
                    if (HexViewerControl.FirstVisibleRowIndex > maxScroll)
                        HexViewerControl.FirstVisibleRowIndex = maxScroll;
                }
            };
        }

        private void GoToAddress_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(AddressBox.Text, System.Globalization.NumberStyles.HexNumber, null, out int addr))
            {
                int row = addr / HexViewerControl.Columns;
                HexViewerControl.FirstVisibleRowIndex = row;
                VerticalScrollBar.Value = row;
            }
            else
            {
                MessageBox.Show("Неверный формат адреса");
            }
        }

        private void FindBytes_Click(object sender, RoutedEventArgs e)
        {
            _searchMatches.Clear();
            _currentMatchIndex = -1;

            string hex = Regex.Replace(SearchBox.Text, @"\s+", "");
            if (hex.Length < 2 || hex.Length % 2 != 0) return;

            byte[] pattern = Enumerable.Range(0, hex.Length / 2)
                .Select(i => Convert.ToByte(hex.Substring(i * 2, 2), 16))
                .ToArray();

            for (int i = 0; i <= _data.Count - pattern.Length; i++)
            {
                if (_data.Skip(i).Take(pattern.Length).SequenceEqual(pattern))
                    _searchMatches.Add(i);
            }

            SearchResultText.Text = $"Найдено: {_searchMatches.Count}";
            if (_searchMatches.Count > 0)
            {
                _currentMatchIndex = 0;
                ScrollToMatch();
            }
        }

        private void ScrollToMatch()
        {
            if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;

            int byteIndex = _searchMatches[_currentMatchIndex];
            int row = byteIndex / HexViewerControl.Columns;

            HexViewerControl.FirstVisibleRowIndex = row;
            VerticalScrollBar.Value = row;
        }

        private void NextMatch_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMatches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
            ScrollToMatch();
        }

        private void PrevMatch_Click(object sender, RoutedEventArgs e)
        {
            if (_searchMatches.Count == 0) return;
            _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
            ScrollToMatch();
        }
    }
}
