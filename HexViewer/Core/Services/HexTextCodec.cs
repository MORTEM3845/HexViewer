using System.Buffers;
using System.Globalization;
using System.Text;
using HexViewer.Items.Enums;

namespace HexViewer.Control.Services;

public static class HexTextCodec
{
    private static readonly Encoding StrictAscii =
        Encoding.GetEncoding(Encoding.ASCII.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictWindows1251 = CreateWindows1251Encoding();
    private static readonly string[] Windows1251Cells = CreateWindows1251Cells();

    public static bool TryEncode(string text, HexTextEncoding encoding, out byte[] bytes)
    {
        try
        {
            bytes = GetEncoding(encoding).GetBytes(text);
            return bytes.Length > 0;
        }
        catch (EncoderFallbackException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    public static string[] DecodeCells(IReadOnlyList<byte> data, int startIndex, int count,
        HexTextEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(data);
        return DecodeCellsCore(data.Count, index => data[index], startIndex, count, encoding);
    }

    internal static string[] DecodeCells(IHexDataSource data, int startIndex, int count,
        HexTextEncoding encoding) =>
        DecodeCellsCore(data.Count, index => data[index], startIndex, count, encoding);

    private static string[] DecodeCellsCore(int dataCount, Func<int, byte> getByte, int startIndex, int count,
        HexTextEncoding encoding)
    {
        startIndex = Math.Clamp(startIndex, 0, dataCount);
        count = Math.Clamp(count, 0, dataCount - startIndex);

        if (encoding == HexTextEncoding.Utf8)
            return DecodeUtf8Cells(dataCount, getByte, startIndex, count);

        var cells = new string[count];
        for (int i = 0; i < count; i++)
        {
            byte value = getByte(startIndex + i);
            cells[i] = encoding == HexTextEncoding.Windows1251
                ? Windows1251Cells[value]
                : value is >= 32 and <= 126 ? ((char)value).ToString() : ".";
        }

        return cells;
    }

    private static string[] DecodeUtf8Cells(int dataCount, Func<int, byte> getByte, int startIndex, int count)
    {
        var cells = Enumerable.Repeat(".", count).ToArray();
        if (count == 0)
            return cells;

        int endIndex = startIndex + count;
        int scanStart = startIndex;
        for (int bytesBack = 0; scanStart > 0 && bytesBack < 3 && IsUtf8Continuation(getByte(scanStart)); bytesBack++)
            scanStart--;

        int scanEnd = Math.Min(dataCount, endIndex + 3);
        Span<byte> buffer = stackalloc byte[4];

        for (int index = scanStart; index < scanEnd;)
        {
            int available = Math.Min(4, scanEnd - index);
            for (int i = 0; i < available; i++)
                buffer[i] = getByte(index + i);

            OperationStatus status = Rune.DecodeFromUtf8(buffer[..available], out Rune rune, out int consumed);
            if (status != OperationStatus.Done || consumed <= 0)
            {
                index++;
                continue;
            }

            if (index >= startIndex && index < endIndex)
                cells[index - startIndex] = ToDisplayText(rune);

            for (int continuation = 1; continuation < consumed; continuation++)
            {
                int continuationIndex = index + continuation;
                if (continuationIndex >= startIndex && continuationIndex < endIndex)
                    cells[continuationIndex - startIndex] = string.Empty;
            }

            index += consumed;
        }

        return cells;
    }

    private static string ToDisplayText(Rune rune)
    {
        UnicodeCategory category = Rune.GetUnicodeCategory(rune);
        return category is UnicodeCategory.Control or UnicodeCategory.Format or
            UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
            ? "."
            : rune.ToString();
    }

    private static bool IsUtf8Continuation(byte value) => (value & 0xC0) == 0x80;

    private static Encoding GetEncoding(HexTextEncoding encoding) => encoding switch
    {
        HexTextEncoding.Utf8 => StrictUtf8,
        HexTextEncoding.Windows1251 => StrictWindows1251,
        _ => StrictAscii
    };

    private static Encoding CreateWindows1251Encoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string[] CreateWindows1251Cells()
    {
        var cells = new string[256];
        for (int value = 0; value < cells.Length; value++)
        {
            try
            {
                string decoded = StrictWindows1251.GetString([(byte)value]);
                cells[value] = decoded.Length == 1 && !char.IsControl(decoded[0]) ? decoded : ".";
            }
            catch (DecoderFallbackException)
            {
                cells[value] = ".";
            }
        }

        return cells;
    }
}
