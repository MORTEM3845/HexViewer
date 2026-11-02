using HexViewer.Control.Services;
using HexViewer.Core;

namespace HexViewer.Control.HexViewer;

public partial class HexViewer
{
    private HexValuePopup? _valuePopup;
    private void OpenValueEditor(int start, int length)
    {
        if (!IsEditable || Data is not { } data || start < 0 || start + length > data.Count || length is < 1 or > 8) return;
        _valuePopup?.Close();
        var known = data; var order = ByteOrder;
        var undo = new Stack<byte[]>(); var redo = new Stack<byte[]>();
        bool Write(byte[] expected, byte[] bytes)
        {
            if (!IsEditable || ByteOrder != order || !ReferenceEquals(Data, known) || start + length > known.Count || !known.Skip(start).Take(length).SequenceEqual(expected)) return false;
            var replacement = known.ToArray(); bytes.CopyTo(replacement, start);
            SetCurrentValue(DataProperty, replacement); known = Data!;
            RaiseDataChangedExternally(); known = Data!;
            return known.Skip(start).Take(length).SequenceEqual(bytes);
        }
        var session = new HexValueEditSession(data.Skip(start).Take(length).ToArray(), order, (expected, bytes) =>
        {
            if (!Write(expected, bytes)) return false;
            undo.Push(expected); redo.Clear(); return true;
        });
        void Restore(Stack<byte[]> from, Stack<byte[]> to)
        {
            if (from.Count == 0) return;
            var current = known.Skip(start).Take(length).ToArray();
            if (Write(current, from.Peek())) { from.Pop(); to.Push(current); }
        }
        _valuePopup = new HexValuePopup(this, session, $"Число · 0x{start:X} · {length} Б · {order}",
            () => Restore(undo, redo), () => Restore(redo, undo));
        _valuePopup.Show();
    }
}
