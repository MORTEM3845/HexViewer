using HexViewer.Items.Enums;
using System.Globalization;
using System.IO;
using System.Text;
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
        private readonly FrameworkElement _owner;
        private Window? _window;

        public bool IsOpen => _tip.IsOpen;

        public TooltipController(FrameworkElement owner)
        {
            _owner = owner;
            _tip = new ToolTip
            {
                FontSize = 15,
                Placement = PlacementMode.Relative,
                PlacementTarget = owner,
                StaysOpen = true
            };

            owner.Loaded += OwnerLoaded;
            owner.Unloaded += OwnerUnloaded;
            owner.IsVisibleChanged += OwnerIsVisibleChanged;
            owner.LostKeyboardFocus += HideNow;
            owner.LostFocus += HideNow;
            owner.MouseLeave += HideNow;
        }

        private void OwnerLoaded(object? sender, RoutedEventArgs e) => AttachWindow(Window.GetWindow(_owner));

        private void OwnerUnloaded(object? sender, RoutedEventArgs e)
        {
            Hide();
            AttachWindow(null);
        }

        private void OwnerIsVisibleChanged(object? sender, DependencyPropertyChangedEventArgs e)
        {
            if (!_owner.IsVisible) Hide();
        }

        private void HideNow(object? sender, EventArgs e) => Hide();

        private void AttachWindow(Window? window)
        {
            if (ReferenceEquals(_window, window)) return;
            if (_window != null)
            {
                _window.Deactivated -= HideNow;
                _window.LocationChanged -= HideNow;
                _window.SizeChanged -= HideNow;
                _window.Closed -= WindowClosed;
            }
            _window = window;
            if (_window != null)
            {
                _window.Deactivated += HideNow;
                _window.LocationChanged += HideNow;
                _window.SizeChanged += HideNow;
                _window.Closed += WindowClosed;
            }
        }

        private void WindowClosed(object? sender, EventArgs e) => AttachWindow(null);

        public async void ShowLater(Point pt, uint value, IList<byte> data, int hoveredIndex, int columns, int firstRow, double cellWidth, double cellHeight, int groupSize, WordSizeEnum wordSize, int visualByteOffset, bool showImmediately = false)
        {
            _pendingText = global::HexViewer.Core.HexTooltip.Format(value, data, hoveredIndex, groupSize, wordSize);

            int hoveredRow = (hoveredIndex + visualByteOffset) / columns;
            int screenRow = hoveredRow - firstRow;

            double y = cellHeight + screenRow * cellHeight + cellHeight / 2 + 12;
            _pendingPt = new Point(pt.X, y);

            // отменяем старую задачу
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            try
            {
                if (!showImmediately)
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

        private static string FormatTextLines(IList<byte> data, int startIndex, int requestedCount) =>
            global::HexViewer.Core.HexTooltip.FormatTextLines(data, startIndex, requestedCount);

    }
}
