using HexViewer.Enums;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HexViewer.Control.Services
{
    public sealed class TooltipController
    {
        private readonly ToolTip _tip;
        private string _pendingText = string.Empty;
        private Point _pendingPt;
        private CancellationTokenSource? _cts;

        public TooltipController(FrameworkElement owner)
        {
            _tip = new ToolTip
            {
                FontSize = 15,
                Placement = PlacementMode.Relative,
                PlacementTarget = owner,
                StaysOpen = true
            };

            // Сразу закрываем тултип при потере фокуса/видимости и т.п.
            void HideNow(object? s, EventArgs e) => Hide();

            owner.Unloaded += HideNow;
            owner.IsVisibleChanged += (_, __) => { if (!owner.IsVisible) Hide(); };
            owner.LostKeyboardFocus += HideNow;
            owner.LostFocus += HideNow;
            owner.MouseLeave += HideNow;

            // Как только появится родительское окно — следим за его активностью
            owner.Loaded += (_, __) =>
            {
                var win = Window.GetWindow(owner);
                if (win == null) return;

                win.Deactivated += HideNow;         // щёлкнули в другое окно
                win.LocationChanged += HideNow;      // окно подвинули — лучше спрятать
                win.SizeChanged += (_, __2) => Hide();

                win.Closed += (_, __2) =>
                {
                    win.Deactivated -= HideNow;
                    win.LocationChanged -= HideNow;
                };
            };
        }

        static string DecLine<TUnsigned, TSigned>(TUnsigned u, TSigned s) where TUnsigned : struct where TSigned : struct, IComparable
        {
            // сравнение с нулём, приводим к long для знаковых
            long sv = Convert.ToInt64(s);
            return sv < 0 ? $"{u} ({s})" : $"{u}";
        }

        public async void ShowLater(Point pt, uint value, int hoveredIndex, int columns, int firstRow, double cellWidth, double cellHeight, int groupSize, WordSizeEnum wordSize)
        {
            int address = wordSize switch
            {
                WordSizeEnum.Byte1 => hoveredIndex,
                WordSizeEnum.Byte2 => hoveredIndex & ~1,
                WordSizeEnum.Byte4 => hoveredIndex & ~3,
                _ => hoveredIndex
            };

            string addrDec = address.ToString(CultureInfo.InvariantCulture);
            string addrHex = $"0x{address:X}";
            string addressText = $@"Addr: {addrDec} ({addrHex})";

            string text;
            switch (wordSize)
            {
                case WordSizeEnum.Byte1:
                    {
                        byte b = (byte)value;
                        sbyte sb = unchecked((sbyte)b);
                        string dec = DecLine(b, sb);
                        string bin = Convert.ToString(b, 2).PadLeft(8, '0');
                        text = $"Dec: {dec}\nHex: 0x{b:X2}\nBin: {bin}";
                        break;
                    }
                case WordSizeEnum.Byte2:
                    {
                        ushort u16 = (ushort)value;
                        short s16 = unchecked((short)u16);
                        string dec = DecLine(u16, s16);
                        string bin16 = Convert.ToString(u16, 2).PadLeft(16, '0');
                        string binGroups = string.Join(" ", Enumerable.Range(0, 4).Select(i => bin16.Substring(i * 4, 4)));
                        text = $"Dec: {dec}\nHex: 0x{u16:X4}\nBin: {binGroups}";
                        break;
                    }
                case WordSizeEnum.Byte4:
                default:
                    {
                        uint u32 = value;
                        int s32 = unchecked((int)value);
                        string dec = DecLine(u32, s32);
                        string bin32 = Convert.ToString(u32, 2).PadLeft(32, '0');
                        string binTetrads = string.Join(" ", Enumerable.Range(0, 8).Select(i => bin32.Substring(i * 4, 4)));
                        float asFloat = BitConverter.ToSingle(BitConverter.GetBytes(u32), 0);
                        text = $"Dec: {dec}\nHex: 0x{u32:X8}\nBin: {binTetrads}\nFloat: {asFloat:G9}";
                        break;
                    }
            }

            _pendingText = text + "\n" + addressText;

            int hoveredRow = hoveredIndex / columns;
            int screenRow = hoveredRow - firstRow;

            double y = cellHeight + screenRow * cellHeight + cellHeight / 2 + 12;
            _pendingPt = new Point(pt.X, y);

            // отменяем старую задачу
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Delay(600, _cts.Token);

                _tip.Content = _pendingText;
                _tip.HorizontalOffset = _pendingPt.X;
                _tip.VerticalOffset = _pendingPt.Y;
                _tip.IsOpen = true;
            }
            catch (TaskCanceledException)
            {
                // отменили показ — ничего не делаем
            }
        }

        public void Hide()
        {
            _cts?.Cancel();
            _tip.IsOpen = false;
            _pendingText = "";
        }
    }
}
