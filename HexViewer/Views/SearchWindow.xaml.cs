using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HexViewer.Items;

namespace HexViewer;

public partial class SearchWindow : Window
{
    public event EventHandler? SearchChanged;
    public event EventHandler? FindRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;

    public SearchValueKind SelectedKind { get; private set; } = SearchValueKind.Hex;
    public SearchValueSize SelectedSize { get; private set; } = SearchValueSize.Auto;
    internal bool SearchFromCurrentAddress { get; private set; }

    public string Status
    {
        get => ResultText.Text;
        set => ResultText.Text = value;
    }

    public SearchWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => FocusSearchText();
    }

    public bool TryGetPattern(out byte[] pattern)
    {
        SearchValueSize size = SelectedKind == SearchValueKind.Decimal
            ? SelectedSize
            : SearchValueSize.Auto;
        bool parsed = SearchPatternParser.TryParse(SearchTextBox.Text, SelectedKind, size,
            out pattern, out string error);
        if (!parsed)
            Status = error;
        return parsed;
    }

    public void FocusSearchText()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void Find_Click(object sender, RoutedEventArgs e) => FindRequested?.Invoke(this, EventArgs.Empty);
    private void Next_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void Previous_Click(object sender, RoutedEventArgs e) => PreviousRequested?.Invoke(this, EventArgs.Empty);
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Status = string.Empty;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchKind_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out SearchValueKind kind))
            return;

        SelectedKind = kind;
        if (!IsInitialized)
            return;

        SearchSizePanel.Visibility = kind == SearchValueKind.Decimal
            ? Visibility.Visible
            : Visibility.Collapsed;
        Status = string.Empty;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchSize_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } ||
            !Enum.TryParse(tag, out SearchValueSize size))
            return;

        SelectedSize = size;
        if (!IsInitialized)
            return;

        Status = string.Empty;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchStart_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || !bool.TryParse(tag, out bool fromCurrent))
            return;

        SearchFromCurrentAddress = fromCurrent;
        if (!IsInitialized)
            return;

        Status = string.Empty;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                PreviousRequested?.Invoke(this, EventArgs.Empty);
            else
                NextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
