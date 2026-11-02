using System.Globalization;
using System.Text;

namespace HexViewer.Items;

public static class HexParser
{
    public static bool TryParseAddress(string? text, out int address)
    {
        address = 0;
        string value = (text ?? string.Empty).Trim();
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        return value.Length > 0 && int.TryParse(value, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out address) && address >= 0;
    }

    public static bool TryParseUInt32(string? text, out uint value)
    {
        value = 0;
        string input = (text ?? string.Empty).Trim();
        if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) input = input[2..];
        return input.Length > 0 && uint.TryParse(input, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseBytes(string? text, out byte[] bytes)
    {
        var cleaned = new StringBuilder((text ?? string.Empty).Length);
        foreach (char ch in text ?? string.Empty)
        {
            if (Uri.IsHexDigit(ch)) cleaned.Append(ch);
            else if (!char.IsWhiteSpace(ch) && ch is not (',' or ';' or '-' or '_'))
            {
                bytes = Array.Empty<byte>();
                return false;
            }
        }

        if (cleaned.Length == 0 || cleaned.Length % 2 != 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = Convert.FromHexString(cleaned.ToString());
        return true;
    }
}
