using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HexViewer.Core;

namespace HexViewer.Control.Services;

internal sealed class HexValuePopup
{
    private readonly Popup _popup;
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private bool _changing;

    public HexValuePopup(FrameworkElement owner, HexValueEditSession session, string title, Action undo, Action redo)
    {
        var root = new StackPanel { Width = session.Length > 4 ? 760 : 420, Margin = new Thickness(10) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var icon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M29.590818,0C30.206963,0 30.823227,0.23449993 31.292229,0.70349979 32.229256,1.6405001 32.229256,3.1605005 31.292229,4.1004992L19.398003,15.998498 31.292229,27.892495C32.229256,28.829499 32.229256,30.349496 31.292229,31.294496 30.354227,32.2315 28.827164,32.2315 27.890139,31.294496L15.997988,19.399499 4.1047992,31.296502C3.1677734,32.234498 1.6397347,32.234498 0.70276969,31.296502 -0.23425656,30.352502 -0.23425656,28.832498 0.70276969,27.894502L12.596934,15.998498 0.70276969,4.1024981C-0.23425656,3.1654978 -0.23425656,1.6454973 0.70276969,0.7054987 1.6397347,-0.23150158 3.1677734,-0.23150158 4.1047992,0.7054987L15.99695,12.597497 27.890139,0.70349979C28.35865,0.23449993,28.974674,0,29.590818,0z"),
            Width = 12, Height = 12, Stretch = Stretch.Uniform
        };
        icon.SetBinding(System.Windows.Shapes.Shape.FillProperty, new System.Windows.Data.Binding(nameof(Button.Foreground))
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        var close = new Button
        {
            Content = icon, Width = 28, Height = 28, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Закрыть (Esc)"
        };
        System.Windows.Automation.AutomationProperties.SetName(close, "Закрыть");
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }); root.Children.Add(heading);
        foreach (var format in session.Formats)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            row.Children.Add(new TextBlock { Text = format == "SIGNED" ? "DEC ±" : format, Width = 60, VerticalAlignment = VerticalAlignment.Center });
            var field = new TextBox { Text = session.Format(format), FontFamily = new FontFamily("Consolas"), FontSize = 14, Padding = new Thickness(3) };
            _fields.Add(format, field); row.Children.Add(field); root.Children.Add(row);
            field.TextChanged += (_, _) =>
            {
                if (_changing) return;
                if (!session.TrySet(format, field.Text, out var error)) { _error.Text = error; return; }
                _error.Text = ""; _changing = true;
                try { foreach (var other in _fields) if (other.Key != format) other.Value.Text = session.Format(other.Key); }
                finally { _changing = false; }
            };
        }
        root.Children.Add(_error);
        _popup = new Popup
        {
            PlacementTarget = owner, Placement = PlacementMode.MousePoint, StaysOpen = false, AllowsTransparency = true,
            Child = new Border { Child = root, Background = SystemColors.WindowBrush, BorderBrush = SystemColors.ActiveBorderBrush, BorderThickness = new Thickness(1) }
        };
        close.Click += (_, _) => { Close(); owner.Focus(); };
        root.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); owner.Focus(); e.Handled = true; }
            else if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && e.Key is Key.Z or Key.Y)
            {
                Close();
                if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) redo(); else undo();
                owner.Focus(); e.Handled = true;
            }
        };
        owner.Unloaded += Unloaded;
        _popup.Closed += (_, _) => owner.Unloaded -= Unloaded;
        void Unloaded(object? sender, RoutedEventArgs e) => Close();
    }
    public void Show()
    {
        _popup.IsOpen = true;
        var first = _fields.GetValueOrDefault("Float") ?? _fields["HEX"];
        first.Dispatcher.BeginInvoke(new Action(() => { if (_popup.IsOpen) { first.Focus(); first.SelectAll(); } }));
    }
    public void Close() => _popup.IsOpen = false;
}
