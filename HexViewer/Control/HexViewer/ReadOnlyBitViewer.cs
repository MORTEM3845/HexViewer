using System.Globalization;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HexViewer.Control.HexViewer;

/// <summary>
/// Lightweight single-row view of a packed little-endian value. The most
/// significant visible bit is rendered first; unused high bits are omitted.
/// </summary>
public sealed class ReadOnlyBitViewer : System.Windows.Controls.Control
{
    private const double GroupSpacing = 6d;
    private readonly TextBlock _tooltipText = new();

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(IList<byte>), typeof(ReadOnlyBitViewer),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTooltipDataChanged));

    public static readonly DependencyProperty BitCountProperty = DependencyProperty.Register(
        nameof(BitCount), typeof(int), typeof(ReadOnlyBitViewer),
        new FrameworkPropertyMetadata(1,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            OnTooltipDataChanged, (_, value) => Math.Max(1, (int)value)));

    public static readonly DependencyProperty CellWidthProperty = DependencyProperty.Register(
        nameof(CellWidth), typeof(double), typeof(ReadOnlyBitViewer),
        new FrameworkPropertyMetadata(14d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => double.IsFinite((double)value) ? Math.Max(1d, (double)value) : 1d));

    public static readonly DependencyProperty CellHeightProperty = DependencyProperty.Register(
        nameof(CellHeight), typeof(double), typeof(ReadOnlyBitViewer),
        new FrameworkPropertyMetadata(22d,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null, (_, value) => double.IsFinite((double)value) ? Math.Max(1d, (double)value) : 1d));

    public ReadOnlyBitViewer()
    {
        Focusable = false;
        FontFamily = new FontFamily("Consolas");
        ToolTip = new ToolTip { Content = _tooltipText };
        ToolTipService.SetInitialShowDelay(this, 600);
    }

    public IList<byte>? Data
    {
        get => (IList<byte>?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public int BitCount
    {
        get => (int)GetValue(BitCountProperty);
        set => SetValue(BitCountProperty, value);
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

    protected override Size MeasureOverride(Size constraint)
    {
        var bitCount = Math.Min(BitCount, (Data?.Count ?? 0) * 8);
        return new Size(bitCount * CellWidth + GroupCount(bitCount) * GroupSpacing, CellHeight);
    }

    protected override void OnToolTipOpening(System.Windows.Controls.ToolTipEventArgs e)
    {
        UpdateTooltipText();
        base.OnToolTipOpening(e);
    }

    private static void OnTooltipDataChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e) =>
        ((ReadOnlyBitViewer)owner).UpdateTooltipText();

    private void UpdateTooltipText() => _tooltipText.Text = FormatTooltip(Data, BitCount);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bits = FormatBits(Data, BitCount);
        if (bits.Length == 0) return;

        drawingContext.DrawRectangle(Brushes.Transparent, null,
            new Rect(0, 0, RenderSize.Width, RenderSize.Height));

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var index = 0; index < bits.Length; index++)
        {
            var text = new FormattedText(bits[index].ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, FontSize, Foreground, pixelsPerDip);
            drawingContext.DrawText(text, new Point(
                index * CellWidth + GroupsBefore(index, bits.Length) * GroupSpacing
                    + (CellWidth - text.Width) / 2,
                (CellHeight - text.Height) / 2));
        }
    }

    internal static string FormatBits(IList<byte>? data, int bitCount)
    {
        if (data is not { Count: > 0 } || bitCount <= 0) return string.Empty;
        var visibleBits = Math.Min(bitCount, data.Count * 8);
        return string.Create(visibleBits, (data, visibleBits), static (output, state) =>
        {
            for (var displayIndex = 0; displayIndex < state.visibleBits; displayIndex++)
            {
                var sourceBit = state.visibleBits - displayIndex - 1;
                output[displayIndex] = (state.data[sourceBit / 8] & 1 << sourceBit % 8) == 0 ? '0' : '1';
            }
        });
    }

    internal static string FormatTooltip(IList<byte>? data, int bitCount)
    {
        var bits = FormatBits(data, bitCount);
        if (bits.Length == 0) return string.Empty;

        var byteCount = (bits.Length + 7) / 8;
        var bytes = new byte[byteCount];
        for (var index = 0; index < byteCount; index++) bytes[index] = data![index];
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: false)
            & ((BigInteger.One << bits.Length) - 1);
        var hex = value.ToString("X", CultureInfo.InvariantCulture).TrimStart('0');
        if (hex.Length == 0) hex = "0";
        var firstGroupLength = bits.Length % 4 == 0 ? 4 : bits.Length % 4;
        var groups = new List<string> { bits[..firstGroupLength] };
        for (var index = firstGroupLength; index < bits.Length; index += 4)
            groups.Add(bits.Substring(index, 4));
        return $"Dec: {value.ToString(CultureInfo.InvariantCulture)}\nHex: 0x{hex}\nBin: {string.Join(" ", groups)}";
    }

    private static int GroupCount(int bitCount) => bitCount > 0 ? (bitCount - 1) / 4 : 0;

    private static int GroupsBefore(int index, int bitCount) =>
        (index + (4 - bitCount % 4) % 4) / 4;
}
