using MessageBox = System.Windows.MessageBox;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HexViewer.Items;

namespace HexViewer.Control.HexViewer;

/// <summary>
/// Lightweight single-row hexadecimal view without editing, hover highlighting
/// or the caches used by the interactive viewer. Tooltips and the context menu
/// are created only while the user interacts with the control.
/// </summary>
public class ReadOnlyHexViewer : System.Windows.Controls.Control
{
    private int _hoveredIndex = -1;

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(IList<byte>), typeof(ReadOnlyHexViewer),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTooltipDataChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(ReadOnlyHexViewer),
        new FrameworkPropertyMetadata(1,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => Math.Max(1, (int)value)));

    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(ReadOnlyHexViewer),
        new FrameworkPropertyMetadata(28d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => double.IsFinite((double)value) ? Math.Max(1d, (double)value) : 1d));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(ReadOnlyHexViewer),
        new FrameworkPropertyMetadata(22d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => double.IsFinite((double)value) ? Math.Max(1d, (double)value) : 1d));

    public ReadOnlyHexViewer()
    {
        Focusable = false;
        FontFamily = new FontFamily("Consolas");
        ToolTipService.SetInitialShowDelay(this, 600);
        ToolTipService.SetBetweenShowDelay(this, 0);
    }

    public IList<byte>? Data
    {
        get => (IList<byte>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public double CellWidth
    {
        get => (double)GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => (double)GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    protected override Size MeasureOverride(Size constraint) => new(Columns * CellWidth, CellHeight);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Data is not { Count: > 0 } data) return;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var count = Math.Min(Columns, data.Count);

        for (var column = 0; column < count; column++)
        {
            var text = new FormattedText(data[column].ToString("X2", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, FontSize, Foreground,
                pixelsPerDip);
            drawingContext.DrawText(text, new Point(
                column * CellWidth + (CellWidth - text.Width) / 2,
                (CellHeight - text.Height) / 2));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = GetByteIndex(e.GetPosition(this).X, Columns, Data?.Count ?? 0, CellWidth);
        if (index == _hoveredIndex) return;

        _hoveredIndex = index;
        UpdateTooltip();
    }

    private static void OnTooltipDataChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e) =>
        ((ReadOnlyHexViewer)owner).UpdateTooltip();

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        UpdateTooltip();
        base.OnToolTipOpening(e);
    }

    private void UpdateTooltip()
    {
        if (Data is { } data && _hoveredIndex >= 0 && _hoveredIndex < Math.Min(Columns, data.Count))
            ToolTip = FormatTooltip(data, _hoveredIndex);
        else
        {
            _hoveredIndex = -1;
            ClearValue(ToolTipProperty);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredIndex = -1;
        ClearValue(ToolTipProperty);
    }

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseRightButtonUp(e);

        if (Data is not { Count: > 0 } data) return;

        _hoveredIndex = -1;
        ClearValue(ToolTipProperty);
        var index = GetByteIndex(e.GetPosition(this).X, Columns, data.Count, CellWidth);
        var menu = CreateContextMenu(data, index);
        menu.Closed += (_, _) => menu.PlacementTarget = null;
        menu.PlacementTarget = this;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private ContextMenu CreateContextMenu(IList<byte> data, int index)
    {
        var menu = new ContextMenu { FontSize = 15 };

        var copyByte = new MenuItem { Header = "Копировать байт", IsEnabled = index >= 0 };
        copyByte.Click += (_, _) =>
        {
            if (index >= 0 && index < data.Count)
                TrySetClipboardText(data[index].ToString("X2", CultureInfo.InvariantCulture));
        };

        var copyAll = new MenuItem { Header = "Копировать все байты" };
        copyAll.Click += (_, _) => TrySetClipboardText(FormatHex.BytesToHexBlockString(data));

        var openViewer = new MenuItem { Header = "Открыть просмотр" };
        openViewer.Click += (_, _) =>
        {
            new global::HexViewer.HexViewerWindow(data)
            {
                Owner = Window.GetWindow(this)
            }.Show();
        };

        menu.Items.Add(copyByte);
        menu.Items.Add(copyAll);
        menu.Items.Add(new Separator());
        menu.Items.Add(openViewer);
        return menu;
    }

    private static int GetByteIndex(double x, int columns, int dataCount, double cellWidth)
    {
        if (!double.IsFinite(x) || x < 0 || columns <= 0 || dataCount <= 0 ||
            !double.IsFinite(cellWidth) || cellWidth <= 0)
            return -1;

        var index = (int)(x / cellWidth);
        return index < Math.Min(columns, dataCount) ? index : -1;
    }

    private static string FormatTooltip(IList<byte> data, int index)
    {
        var value = data[index];
        var signed = unchecked((sbyte)value);
        var decimalText = signed < 0 ? $"{value} ({signed})" : value.ToString(CultureInfo.InvariantCulture);
        var binaryText = Convert.ToString(value, 2).PadLeft(8, '0');
        return $"Dec: {decimalText}\nHex: 0x{value:X2}\nBin: {binaryText}\nAddr: {index} (0x{index:X})";
    }

    private static void TrySetClipboardText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            MessageBox.Show("Буфер обмена временно недоступен.", "Просмотр HEX",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
