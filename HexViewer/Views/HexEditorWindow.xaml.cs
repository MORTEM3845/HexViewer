using MessageBox = System.Windows.MessageBox;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HexViewer.Items;
using HexViewer.Items.Enums;

namespace HexViewer;

public partial class HexEditorWindow : Window
{
    private readonly List<int> _searchMatches = new();
    private int _currentMatchIndex = -1;
    private bool _updatingScroll;
    private byte[]? _savedBytes;
    private int _searchPatternLength;
    private CancellationTokenSource? _searchCancellation;
    private SearchWindow? _searchWindow;
    public bool IsSaved { get; private set; }

    public HexDisplayMode DisplayMode
    {
        get => Editor.DisplayMode;
        set => Editor.DisplayMode = value;
    }

    public bool ShowAscii
    {
        get => Editor.ShowAscii;
        set => Editor.ShowAscii = value;
    }

    public HexTextEncoding TextEncoding
    {
        get => Editor.TextEncoding;
        set
        {
            Editor.TextEncoding = value;
            if (TextEncodingBox != null)
                TextEncodingBox.SelectedIndex = EncodingToSelectedIndex(value);
        }
    }

    public HexEditorWindow() : this(Array.Empty<byte>()) { }

    public HexEditorWindow(IEnumerable<byte>? data)
    {
        InitializeComponent();

        Editor.SetBytes(data ?? Array.Empty<byte>());

        Editor.FirstVisibleRowChanged += (_, _) => SyncScrollBarFromEditor();
        Editor.TotalRowsChanged += (_, _) => UpdateScrollBar();
        Editor.SelectionChanged += (_, _) => UpdateStatus();
        Editor.ModifiedChanged += (_, _) => UpdateTitleAndStatus();
        Editor.InsertModeChanged += (_, _) => UpdateStatus();
        Editor.DataEdited += (_, _) => { ClearSearch(); UpdateTitleAndStatus(); };

        VerticalScrollBar.ValueChanged += (_, _) => SyncEditorFromScrollBar();

        Loaded += (_, _) =>
        {
            UpdateScrollBar();
            UpdateTitleAndStatus();
            UpdateSaveButton();
            Editor.Focus();
        };

        SizeChanged += (_, _) => UpdateScrollBar();
    }

    private void Save_Click(object sender, RoutedEventArgs e) => SaveEditorState();

    private void SaveCommand_Executed(object sender, ExecutedRoutedEventArgs e) => SaveEditorState();

    private void SaveEditorState()
    {
        if (!Editor.IsModified)
            return;

        _savedBytes = Editor.GetBytes();
        IsSaved = true;
        Editor.MarkSaved();
        UpdateTitleAndStatus();
    }

    private void FindCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        OpenSearchWindow();
    }

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
        searchWindow.FindRequested += Find;
        searchWindow.NextRequested += (_, _) => ShowNextMatch();
        searchWindow.PreviousRequested += (_, _) => ShowPreviousMatch();
        searchWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_searchWindow, searchWindow))
                _searchWindow = null;
        };
        searchWindow.Show();
    }

    private void GoToCommand_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    public byte[] GetBytes() => _savedBytes ?? Editor.GetBytes();

    private void UpdateSaveButton()
    {
        if (SaveButton != null)
            SaveButton.IsEnabled = Editor.IsModified;
    }

    private void GoToAddress_Click(object sender, RoutedEventArgs e)
    {
        if (!HexParser.TryParseAddress(AddressBox.Text, out int address))
        {
            MessageBox.Show(this, "Неверный адрес. Пример: 1A0 или 0x1A0.", "HEX редактор", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Editor.GoToByte(address);
        UpdateScrollBar();
    }

    private async void Find(object? sender, EventArgs e)
    {
        _searchMatches.Clear();
        _currentMatchIndex = -1;

        if (_searchWindow == null || !_searchWindow.TryGetPattern(out var pattern))
            return;

        int startIndex = _searchWindow.SearchFromCurrentAddress ? Editor.CaretIndex : 0;

        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        CancellationToken searchToken = _searchCancellation.Token;
        try
        {
            _searchWindow.Status = "Поиск…";
            var data = Editor.GetBytes();
            var matches = await Task.Run(() => ByteSearch.FindMatchesFrom(data, pattern, startIndex,
                ByteSearch.DefaultMatchLimit, searchToken));
            searchToken.ThrowIfCancellationRequested();
            _searchMatches.AddRange(matches);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_searchWindow == null)
            return;

        _searchWindow.Status = _searchMatches.Count == ByteSearch.DefaultMatchLimit
            ? $"Найдено: не менее {_searchMatches.Count:N0} (достигнут лимит)"
            : $"Найдено: {_searchMatches.Count}";
        if (_searchMatches.Count == 0) return;

        _currentMatchIndex = 0;
        _searchPatternLength = pattern.Length;
        ShowCurrentMatch(pattern.Length);
    }

    private void ShowNextMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex + 1) % _searchMatches.Count;
        ShowCurrentMatch(_searchPatternLength);
    }

    private void ShowPreviousMatch()
    {
        if (_searchMatches.Count == 0) return;
        _currentMatchIndex = (_currentMatchIndex - 1 + _searchMatches.Count) % _searchMatches.Count;
        ShowCurrentMatch(_searchPatternLength);
    }

    private void ColumnsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Editor == null || ColumnsBox.SelectedItem is not ComboBoxItem item) return;
        if (int.TryParse(item.Content?.ToString(), out int columns))
        {
            Editor.Columns = columns;
            UpdateScrollBar();
        }
    }

    private void TextEncodingBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Editor == null || TextEncodingBox.SelectedItem is not ComboBoxItem item ||
            !Enum.TryParse(item.Tag?.ToString(), out HexTextEncoding encoding))
            return;

        Editor.TextEncoding = encoding;
    }

    private static int EncodingToSelectedIndex(HexTextEncoding encoding) => encoding switch
    {
        HexTextEncoding.Utf8 => 1,
        HexTextEncoding.Windows1251 => 2,
        _ => 0
    };

    private void ShowCurrentMatch(int length)
    {
        if (_currentMatchIndex < 0 || _currentMatchIndex >= _searchMatches.Count) return;
        int index = _searchMatches[_currentMatchIndex];
        Editor.SelectRange(index, Math.Max(1, length));
        Editor.ScrollToByte(index, center: true);
        if (_searchWindow != null)
            _searchWindow.Status = $"Найдено: {_searchMatches.Count}, текущий: {_currentMatchIndex + 1}";
        UpdateScrollBar();
    }

    private void ClearSearch()
    {
        _searchCancellation?.Cancel();
        _searchMatches.Clear();
        _currentMatchIndex = -1;
        _searchPatternLength = 0;
        if (_searchWindow != null)
            _searchWindow.Status = string.Empty;
    }

    private void SyncEditorFromScrollBar()
    {
        if (_updatingScroll) return;
        _updatingScroll = true;
        Editor.FirstVisibleRowIndex = (int)VerticalScrollBar.Value;
        _updatingScroll = false;
    }

    private void SyncScrollBarFromEditor()
    {
        if (_updatingScroll) return;
        _updatingScroll = true;
        if ((int)VerticalScrollBar.Value != Editor.FirstVisibleRowIndex)
            VerticalScrollBar.Value = Editor.FirstVisibleRowIndex;
        _updatingScroll = false;
    }

    private void UpdateScrollBar()
    {
        int rows = Math.Max(1, (int)Math.Ceiling(Editor.ByteCount / (double)Math.Max(1, Editor.Columns)));
        int max = Math.Max(0, rows - Math.Max(1, Editor.VisibleRows));

        VerticalScrollBar.Minimum = 0;
        VerticalScrollBar.Maximum = max;
        VerticalScrollBar.SmallChange = 1;
        VerticalScrollBar.LargeChange = Math.Max(1, Editor.VisibleRows);
        VerticalScrollBar.ViewportSize = Math.Max(1, Editor.VisibleRows);

        if (VerticalScrollBar.Value > max) VerticalScrollBar.Value = max;
        SyncScrollBarFromEditor();
        UpdateStatus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Editor.IsModified)
        {
            base.OnClosing(e);
            return;
        }

        var result = MessageBox.Show(this, "Сохранить изменения?", "HEX редактор",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.Yes)
            SaveEditorState();

        base.OnClosing(e);
    }

    private void UpdateTitleAndStatus()
    {
        Title = $"HEX редактор{(Editor.IsModified ? " *" : string.Empty)}";
        UpdateStatus();
        UpdateSaveButton();
    }

    private void UpdateStatus()
    {
        FileStatusText.Text = $"Размер: {Editor.ByteCount.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"))} Б";
        CaretStatusText.Text = $"Адрес: 0x{Editor.CaretIndex:X} ({Editor.CaretIndex})";
        SelectionStatusText.Text = Editor.SelectionLength > 0 ? $"Выделено: {Editor.SelectionLength} Б" : "Выделено: нет";
        ModifiedStatusText.Text = $"{(Editor.IsModified ? "изменён" : "без изменений")}, режим: {(Editor.InsertMode ? "вставка" : "замена")}";
    }

}
