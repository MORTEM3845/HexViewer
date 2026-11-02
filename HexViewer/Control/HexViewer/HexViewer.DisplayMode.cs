using System.Windows;
using HexViewer.Items.Enums;

namespace HexViewer.Control.HexViewer;

public partial class HexViewer
{
    /// <summary>Word рисует слитное HEX-слово без побайтовых украшений и подсказок.</summary>
    public HexDisplayMode DisplayMode
    {
        get => (HexDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(HexDisplayMode), typeof(HexViewer),
        new FrameworkPropertyMetadata(HexDisplayMode.Bytes,
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure,
            OnLayoutPropertyChanged), value => value is HexDisplayMode mode && Enum.IsDefined(mode));

    private bool WordLayout => DisplayMode == HexDisplayMode.Word;
    private bool CompactLayout => IsCompact || WordLayout;
    private bool AsciiLayout => ShowAscii && !WordLayout;
    private double DisplayCellWidth
    {
        get { EnsureTextCache(); return WordLayout ? _texts.Create("00").WidthIncludingTrailingWhitespace : CellWidth * FontSize / 15; }
    }
    private double DisplayGroupSpacing => WordLayout ? 0 : GroupSpacing * FontSize / 15;

    protected override void OnToolTipOpening(System.Windows.Controls.ToolTipEventArgs e)
    {
        if (WordLayout) e.Handled = true;
        else base.OnToolTipOpening(e);
    }
}
