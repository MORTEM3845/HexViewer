using System.Windows;
using System.Windows.Controls;
using HexViewer.Items.Enums;

namespace HexViewer.Control.HexViewer;

public partial class HexViewer
{
    public HexByteOrder ByteOrder
    {
        get => (HexByteOrder)GetValue(ByteOrderProperty);
        set => SetValue(ByteOrderProperty, value);
    }

    public static readonly DependencyProperty ByteOrderProperty = DependencyProperty.Register(
        nameof(ByteOrder), typeof(HexByteOrder), typeof(HexViewer),
        new FrameworkPropertyMetadata(HexByteOrder.LittleEndian,
            FrameworkPropertyMetadataOptions.AffectsRender, OnByteOrderChanged),
        value => value is HexByteOrder.LittleEndian or HexByteOrder.BigEndian);

    public HexByteDisplayOrder ByteDisplayOrder
    {
        get => (HexByteDisplayOrder)GetValue(ByteDisplayOrderProperty);
        set => SetValue(ByteDisplayOrderProperty, value);
    }

    public static readonly DependencyProperty ByteDisplayOrderProperty = DependencyProperty.Register(
        nameof(ByteDisplayOrder), typeof(HexByteDisplayOrder), typeof(HexViewer),
        new FrameworkPropertyMetadata(HexByteDisplayOrder.AddressOrder,
            FrameworkPropertyMetadataOptions.AffectsRender, OnByteOrderChanged),
        value => value is HexByteDisplayOrder.AddressOrder or HexByteDisplayOrder.ReverseWithinWord);

    private static void OnByteOrderChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e) =>
        ((HexViewer)owner).ClearHoverState();

    private void AddByteOrderMenu()
    {
        var menu = (ContextMenu)ContextMenuService.GetContextMenu(this);
        var interpretation = new MenuItem { Header = "Порядок байтов числа" };
        var display = new MenuItem { Header = "Порядок отображения байтов" };
        AddOption(interpretation, "Little-endian — младший байт первый", ByteOrderProperty, HexByteOrder.LittleEndian);
        AddOption(interpretation, "Big-endian — старший байт первый", ByteOrderProperty, HexByteOrder.BigEndian);
        AddOption(display, "Как в данных (по адресам)", ByteDisplayOrderProperty, HexByteDisplayOrder.AddressOrder);
        AddOption(display, "Обратный внутри слова", ByteDisplayOrderProperty, HexByteDisplayOrder.ReverseWithinWord);
        menu.Items.Add(new Separator());
        menu.Items.Add(interpretation);
        menu.Items.Add(display);
    }

    private void AddOption(MenuItem parent, string title, DependencyProperty property, object value)
    {
        var item = new MenuItem { Header = title, IsCheckable = true };
        item.Click += (_, _) => SetCurrentValue(property, value);
        parent.SubmenuOpened += (_, _) => item.IsChecked = Equals(GetValue(property), value);
        parent.Items.Add(item);
    }
}
