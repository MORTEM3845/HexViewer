**Проект библиотека удобного hex просмотрщика**

Реализован сам элемент, который можно вставлять в логи, в месте где нужно сделать запись или чтение hex данных

Примеры использования (MFD Monitor (Монитор МФИ), HSI Monitor (Монитор ПНП-К)):

           <local:HexViewer Data="{Binding RawData, Mode=OneWay}" PreviewMouseWheel="HexVisualViewer_PreviewMouseWheel"
                                  Visibility="{Binding RawData, Converter={StaticResource CollectionCountToVisibilityConverter}}"
                                  IsLoadFromBinVisible="False"/>


            public class CollectionCountToVisibilityConverter : IValueConverter
            {
                public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    if (value is ICollection collection)
                        return collection.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                    return Visibility.Collapsed;
                }

                public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    throw new NotImplementedException();
                }
            }

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



Примеры использования NumericTextBoxTooltip (ValueFormat = dec/hex)
              <StackPanel  Orientation="Horizontal" Margin="10 0 0 0">
                        <TextBlock Text="Адрес чтения (0x):" VerticalAlignment="Center"/>
                        <TextBox Text="{Binding ReadAddress}" Margin="5 0 10 0" VerticalAlignment="Center" Style="{StaticResource CustomTextBoxStyle}" Width="100"
                                 class:NumericTextBoxTooltip.ValueFormat="hex" ToolTipService.InitialShowDelay="500"/>

                        <TextBlock Text="Размер чтения (dec):" VerticalAlignment="Center"/>
                        <TextBox Text="{Binding ReadSize}" Margin="5 0 0 0" VerticalAlignment="Center" 
                                 PreviewTextInput="IntBox_PreviewTextInput" Style="{StaticResource CustomTextBoxStyle}" Width="100"
                                 ContextMenuOpening="TextBox_ContextMenuOpening" ContextMenu="{x:Null}"
                                 class:NumericTextBoxTooltip.ValueFormat="dec" ToolTipService.InitialShowDelay="500"/>
                    </StackPanel>