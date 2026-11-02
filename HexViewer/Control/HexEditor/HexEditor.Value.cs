using System.Windows;
using System.Windows.Controls;
using HexViewer.Control.Services;
using HexViewer.Core;
using HexViewer.Items.Enums;

namespace HexViewer.Control.HexEditor;

public sealed partial class HexEditor
{
    private MenuItem? _valueMenuItem;
    private HexValuePopup? _valuePopup;
    private MenuItem CreateValueByteOrderMenu()
    {
        var menu = new MenuItem { Header = "Порядок байтов числа" };
        foreach (var order in new[] { HexByteOrder.LittleEndian, HexByteOrder.BigEndian })
        {
            var item = new MenuItem { Header = order == HexByteOrder.LittleEndian ? "Младший байт первый (LittleEndian)" : "Старший байт первый (BigEndian)", IsCheckable = true };
            item.Click += (_, _) => ByteOrder = order;
            menu.SubmenuOpened += (_, _) => item.IsChecked = ByteOrder == order;
            menu.Items.Add(item);
        }
        return menu;
    }
    public HexByteOrder ByteOrder
    {
        get => (HexByteOrder)GetValue(ByteOrderProperty);
        set => SetValue(ByteOrderProperty, value);
    }
    public static readonly DependencyProperty ByteOrderProperty = DependencyProperty.Register(
        nameof(ByteOrder), typeof(HexByteOrder), typeof(HexEditor), new PropertyMetadata(HexByteOrder.LittleEndian),
        value => value is HexByteOrder.LittleEndian or HexByteOrder.BigEndian);

    private (int Start, int Length) ValueRange() => HexValueEditSession.ResolveRange(_bytes.Count,
        _selectionStart, _selectionLength, GetContextIndex(), GroupSize, RowColumns);

    private void OpenValueEditor()
    {
        var (start, length) = ValueRange();
        if (length is < 1 or > 8 || !CanReplace(start, length, length)) return;
        _valuePopup?.Close();
        long revision = _revision;
        var order = ByteOrder;
        var session = new HexValueEditSession(_bytes.GetRange(start, length).ToArray(), order, (expected, bytes) =>
        {
            if (_revision != revision || ByteOrder != order || start + length > _bytes.Count || !_bytes.Skip(start).Take(length).SequenceEqual(expected)) return false;
            if (!ReplaceRange(start, length, bytes, createUndo: true)) return false;
            SelectRange(start, length);
            FinishEdit(); revision = _revision;
            return _bytes.Skip(start).Take(length).SequenceEqual(bytes);
        });
        _valuePopup = new HexValuePopup(this, session, $"Число · 0x{start:X} · {length} Б · {order}", Undo, Redo);
        _valuePopup.Show();
    }
}
