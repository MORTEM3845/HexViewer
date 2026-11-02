using System.Globalization;
using System.Text;
using HexViewer.Items.Enums;

namespace HexViewer.Core;

public static class HexTooltip
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
    private static readonly Encoding Windows1251 = CreateWindows1251();

    public static string Format(uint value, IList<byte> data, int address, int count, WordSizeEnum wordSize)
    {
        string text;
        switch (wordSize)
        {
            case WordSizeEnum.Byte1:
                byte b = (byte)value;
                text = $"Dec: {Decimal(b, unchecked((sbyte)b))}\nHex: 0x{b:X2}\nBin: {Convert.ToString(b, 2).PadLeft(8, '0')}";
                break;
            case WordSizeEnum.Byte2:
                ushort u = (ushort)value;
                text = $"Dec: {Decimal(u, unchecked((short)u))}\nHex: 0x{u:X4}\nBin: {Binary(u, 16)}";
                break;
            default:
                text = $"Dec: {Decimal(value, unchecked((int)value))}\nHex: 0x{value:X8}\nBin: {Binary(value, 32)}\nFloat: {BitConverter.Int32BitsToSingle(unchecked((int)value)):G9}";
                break;
        }
        return text + $"\nAddr: {address.ToString(CultureInfo.InvariantCulture)} (0x{address:X})\n" + FormatTextLines(data, address, count);
    }
    private static string Decimal(uint value, long signed) => signed < 0 ? $"{value} ({signed})" : $"{value}";
    private static string Binary(uint value, int bits)
    {
        string binary = Convert.ToString(value, 2).PadLeft(bits, '0');
        return string.Join(" ", Enumerable.Range(0, bits / 4).Select(i => binary.Substring(i * 4, 4)));
    }
    public static string FormatTextLines(IList<byte> data, int startIndex, int requestedCount)
    {
        int count = Math.Clamp(requestedCount, 0, Math.Max(0, data.Count - startIndex));
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = data[startIndex + i];
        return $"Str UTF-8: {Sanitize(Utf8.GetString(bytes))}\nStr Windows-1251: {Sanitize(Windows1251.GetString(bytes))}";
    }
    private static string Sanitize(string value) => string.Concat(value.Select(c => char.IsControl(c) ? '.' : c));
    private static Encoding CreateWindows1251()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ReplacementFallback, new DecoderReplacementFallback("\uFFFD"));
    }
}
