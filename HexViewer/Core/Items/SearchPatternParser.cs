using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace HexViewer.Items;

public enum SearchValueKind
{
    Hex,
    Decimal,
    Float32,
    Utf8,
    Windows1251
}

public enum SearchValueSize
{
    Auto = 0,
    Byte1 = 1,
    Byte2 = 2,
    Byte4 = 4
}

public static class SearchPatternParser
{
    public static bool TryParse(string? text, SearchValueKind kind, out byte[] bytes, out string error)
        => TryParse(text, kind, SearchValueSize.Auto, out bytes, out error);

    public static bool TryParse(string? text, SearchValueKind kind, SearchValueSize size,
        out byte[] bytes, out string error)
    {
        string rawValue = text ?? string.Empty;
        string value = kind is SearchValueKind.Utf8 or SearchValueKind.Windows1251
            ? rawValue
            : rawValue.Trim();
        bytes = Array.Empty<byte>();
        error = string.Empty;

        if (value.Length == 0)
        {
            error = "Введите значение для поиска";
            return false;
        }

        bool parsed = kind switch
        {
            SearchValueKind.Hex => HexParser.TryParseBytes(value, out bytes),
            SearchValueKind.Decimal => TryParseDecimal(value, size, out bytes),
            SearchValueKind.Float32 => TryParseFloat(value, out bytes),
            SearchValueKind.Utf8 => TryEncode(value, Encoding.UTF8, out bytes),
            SearchValueKind.Windows1251 => TryEncode(value, CreateWindows1251Encoding(), out bytes),
            _ => false
        };

        if (!parsed)
            error = GetError(kind, size);

        return parsed;
    }

    private static bool TryParseDecimal(string value, SearchValueSize size, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
            return false;

        if (number < int.MinValue || number > uint.MaxValue)
            return false;

        int byteCount = size == SearchValueSize.Auto ? GetSmallestIntegerSize(number) : (int)size;
        if (!FitsIntegerSize(number, byteCount))
            return false;

        if (byteCount == sizeof(byte))
        {
            bytes = [unchecked((byte)number)];
        }
        else if (byteCount == sizeof(ushort))
        {
            bytes = new byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, unchecked((ushort)number));
        }
        else
        {
            bytes = new byte[sizeof(uint)];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, unchecked((uint)number));
        }

        return true;
    }

    private static int GetSmallestIntegerSize(long number)
    {
        if (FitsIntegerSize(number, sizeof(byte)))
            return sizeof(byte);
        return FitsIntegerSize(number, sizeof(ushort)) ? sizeof(ushort) : sizeof(uint);
    }

    private static bool FitsIntegerSize(long number, int byteCount) => byteCount switch
    {
        sizeof(byte) => number >= sbyte.MinValue && number <= byte.MaxValue,
        sizeof(ushort) => number >= short.MinValue && number <= ushort.MaxValue,
        sizeof(uint) => number >= int.MinValue && number <= uint.MaxValue,
        _ => false
    };

    private static bool TryParseFloat(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out float number) &&
            !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
            !float.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return false;

        bytes = new byte[sizeof(float)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(number));
        return true;
    }

    private static bool TryEncode(string value, Encoding encoding, out byte[] bytes)
    {
        try
        {
            bytes = encoding.GetBytes(value);
            return bytes.Length > 0;
        }
        catch (EncoderFallbackException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static Encoding CreateWindows1251Encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string GetError(SearchValueKind kind, SearchValueSize size) => kind switch
    {
        SearchValueKind.Hex => "Неверный HEX. Пример: DE AD BE EF",
        SearchValueKind.Decimal when size != SearchValueSize.Auto =>
            $"Число не помещается в выбранный размер ({(int)size} байт)",
        SearchValueKind.Decimal => "Введите число от −2147483648 до 4294967295",
        SearchValueKind.Float32 => "Введите 32-битное число с плавающей точкой",
        _ => "Не удалось преобразовать текст в байты"
    };
}
