using System.Globalization;
using HexViewer.Items.Enums;

namespace HexViewer.Core;

/// <summary>Представления числа и замена фиксированного диапазона байтов.</summary>
public sealed class HexValueEditSession
{
    private byte[] _bytes;
    private readonly HexByteOrder _order;
    private readonly Func<byte[], byte[], bool> _replace;
    public int Length => _bytes.Length;
    public IEnumerable<string> Formats => Length switch
    {
        4 => ["HEX", "DEC", "SIGNED", "BIN", "Float"],
        8 => ["HEX", "DEC", "SIGNED", "BIN", "Double"],
        _ => ["HEX", "DEC", "SIGNED", "BIN"]
    };

    public HexValueEditSession(byte[] bytes, HexByteOrder order, Func<byte[], byte[], bool> replace)
    {
        if (bytes.Length is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(bytes));
        _bytes = bytes.ToArray(); _order = order; _replace = replace;
    }

    private ulong Value
    {
        get
        {
            ulong value = 0;
            for (int i = 0; i < Length; i++)
                value |= (ulong)_bytes[i] << ((_order == HexByteOrder.LittleEndian ? i : Length - i - 1) * 8);
            return value;
        }
    }
    private ulong Maximum => Length == 8 ? ulong.MaxValue : (1UL << (Length * 8)) - 1;
    public string Format(string format)
    {
        ulong value = Value;
        return format switch
        {
            "HEX" => value.ToString("X" + Length * 2, CultureInfo.InvariantCulture),
            "DEC" => value.ToString(CultureInfo.InvariantCulture),
            "SIGNED" => unchecked((long)(value << (64 - Length * 8)) >> (64 - Length * 8)).ToString(CultureInfo.InvariantCulture),
            "BIN" => string.Join(" ", Enumerable.Range(0, Length * 2).Select(i =>
                Convert.ToString((int)(value >> ((Length * 2 - i - 1) * 4) & 15), 2).PadLeft(4, '0'))),
            "Float" when Length == 4 => BitConverter.Int32BitsToSingle(unchecked((int)value)).ToString("G9", CultureInfo.CurrentCulture),
            "Double" when Length == 8 => BitConverter.Int64BitsToDouble(unchecked((long)value)).ToString("G17", CultureInfo.CurrentCulture),
            _ => throw new ArgumentException("Неизвестный формат числа.", nameof(format))
        };
    }

    public bool TrySet(string format, string text, out string error)
    {
        error = "";
        text = text.Trim();
        ulong value;
        if (format is "HEX" or "BIN")
        {
            var digits = string.Concat(text.Where(c => !char.IsWhiteSpace(c) && c != '_'));
            if (digits.StartsWith(format == "HEX" ? "0x" : "0b", StringComparison.OrdinalIgnoreCase)) digits = digits[2..];
            var radix = format == "HEX" ? 16 : 2;
            value = 0;
            if (digits.Length == 0) return Invalid(out error);
            foreach (var c in digits)
            {
                int digit = c is >= '0' and <= '9' ? c - '0' : char.ToUpperInvariant(c) - 'A' + 10;
                if (digit < 0 || digit >= radix || value > (Maximum - (ulong)digit) / (ulong)radix)
                    return Invalid(out error);
                value = value * (ulong)radix + (ulong)digit;
            }
        }
        else if (format == "Float" && Length == 4)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                && !float.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return Invalid(out error);
            value = unchecked((uint)BitConverter.SingleToInt32Bits(number));
        }
        else if (format == "Double" && Length == 8)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number)
                && !double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return Invalid(out error);
            value = unchecked((ulong)BitConverter.DoubleToInt64Bits(number));
        }
        else if (format == "SIGNED")
        {
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var signed)) return Invalid(out error);
            long minimum = Length == 8 ? long.MinValue : -(1L << (Length * 8 - 1));
            long maximum = Length == 8 ? long.MaxValue : (1L << (Length * 8 - 1)) - 1;
            if (signed < minimum || signed > maximum) return Invalid(out error);
            value = unchecked((ulong)signed) & Maximum;
        }
        else if (format != "DEC" || !ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value > Maximum)
            return Invalid(out error);

        var bytes = new byte[Length];
        for (int i = 0; i < Length; i++) bytes[i] = (byte)(value >> ((_order == HexByteOrder.LittleEndian ? i : Length - i - 1) * 8));
        if (bytes.AsSpan().SequenceEqual(_bytes)) return true;
        if (!_replace(_bytes.ToArray(), bytes))
        {
            error = "Данные изменились или диапазон защищён. Откройте редактор снова.";
            return false;
        }
        _bytes = bytes;
        return true;
    }
    private bool Invalid(out string error)
    {
        error = $"Введите число, представимое в {Length * 8} битах.";
        return false;
    }

    public static (int Start, int Length) ResolveRange(int count, int selectionStart, int selectionLength,
        int index, int groupSize, int columns, int visualOffset = 0)
    {
        if (selectionLength > 0)
            return selectionStart >= 0 && (long)selectionStart + selectionLength <= count ? (selectionStart, selectionLength) : (-1, 0);
        if (index < 0 || index >= count) return (-1, 0);
        columns = Math.Max(1, columns); groupSize = Math.Max(1, groupSize);
        int display = index + visualOffset, row = display / columns * columns;
        int column = display % columns / groupSize * groupSize;
        int start = Math.Max(0, row + column - visualOffset);
        int end = (int)Math.Min(count, (long)row + Math.Min(columns, column + groupSize) - visualOffset);
        return (start, end - start);
    }
}
