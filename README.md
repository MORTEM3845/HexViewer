# HexViewer

HexViewer is a reusable WPF UI control for displaying and inspecting binary data in hexadecimal format.

The control is designed for debugging, protocol analysis, and low-level data inspection, with a focus on clarity, simplicity, and safe read-only interaction.

---

## Features

- Display raw binary data in hexadecimal representation
- Designed as a reusable WPF control
- Read-only by default, suitable for inspection and diagnostics
- Supports binding to binary data from ViewModels
- Can be embedded into existing WPF applications
- Optional UI elements can be enabled or disabled

---

## Usage

HexViewer is intended to be used as a UI element inside a WPF application.

### XAML example

```xml
<local:HexViewer
    Data="{Binding RawData, Mode=OneWay}"
    PreviewMouseWheel="HexVisualViewer_PreviewMouseWheel"
    Visibility="{Binding RawData, Converter={StaticResource CollectionCountToVisibilityConverter}}"
    IsLoadFromBinVisible="False" />

 PreviewMouseWheel="HexVisualViewer_PreviewMouseWheel":
      private void HexVisualViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
      {
          DependencyObject parent = (DependencyObject)sender;
          while (parent != null && !(parent is ScrollViewer))
              parent = VisualTreeHelper.GetParent(parent);

          if (parent is ScrollViewer scrollViewer)
          {
              double scrollStep = e.Delta / 40.0;
              scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - scrollStep);
              e.Handled = true;
          }
      }
