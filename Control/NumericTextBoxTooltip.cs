using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace HexViewer.Control
{
    public static class NumericTextBoxTooltip
    {
        public static readonly DependencyProperty ValueFormatProperty =
            DependencyProperty.RegisterAttached(
                "ValueFormat",
                typeof(string),
                typeof(NumericTextBoxTooltip),
                new PropertyMetadata(null, OnValueFormatChanged));

        public static void SetValueFormat(DependencyObject element, string value)
            => element.SetValue(ValueFormatProperty, value);

        public static string GetValueFormat(DependencyObject element)
            => (string)element.GetValue(ValueFormatProperty);

        private static void OnValueFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox tb)
            {
                tb.ToolTip = string.Empty;

                tb.ToolTipOpening += (s, args) =>
                {
                    string mode = (string)e.NewValue ?? "dec";

                    uint value;
                    bool ok = mode.ToLowerInvariant() switch
                    {
                        "hex" => uint.TryParse(tb.Text.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value),
                        "dec" => uint.TryParse(tb.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
                        _ => uint.TryParse(tb.Text, out value)
                    };

                    if (!ok)
                    {
                        tb.ToolTip = new TextBlock
                        {
                            Text = "Нет данных",
                            FontSize = 15,
                        };
                        return;
                    }

                    string bin = Convert.ToString(value, 2).PadLeft(32, '0');
                    string binGroups = string.Join(" ", Enumerable.Range(0, 8).Select(i => bin.Substring(i * 4, 4)));
                    float asFloat = BitConverter.ToSingle(BitConverter.GetBytes(value), 0);

                    string text = mode.ToLowerInvariant() switch
                    {
                        "dec" => $"Dec: {value}\nHex: 0x{value:X8}\nBin: {binGroups}\nFloat: {asFloat:G9}",
                        "hex" => $"Dec: {value}\nHex: 0x{value:X8}\nBin: {binGroups}\nFloat: {asFloat:G9}",
                        _ => $"Value: {value}\nFloat: {asFloat:G9}"
                    };

                    tb.ToolTip = new TextBlock
                    {
                        Text = text,
                        FontSize = 15,
                        TextWrapping = TextWrapping.Wrap
                    };
                };
            }
        }
    }
}
