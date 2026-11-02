using MessageBox = System.Windows.MessageBox;
using HexViewer.Items;
using HexViewer.Items.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace HexViewer.Control.Services
{
    /// <summary>
    /// Контроллер контекстного меню без DI/интерфейсов.
    /// Работает напрямую с Clipboard и Win32 диалогами.
    /// </summary>
    public sealed class ContextMenuController
    {
        private readonly FrameworkElement _owner;
        private const int MaxClipboardBytes = 1_000_000;

        private bool _isEditable = true;

        private readonly ContextMenu _menu = new() { FontSize = 15 };
        private readonly MenuItem _miSaveAll = new() { Header = "Сохранить как бинарный файл" };
        private readonly MenuItem _miCopyAll = new() { Header = "Копировать в буфер обмена" };
        private readonly MenuItem _miCopyTetrad = new() { Header = "Копировать выделенное в буфер обмена" };
        private readonly MenuItem _miLoadFromBin = new() { Header = "Загрузить из бинарного файла" };

        private readonly MenuItem _miWordSize = new() { Header = "Размер слова" };
        private readonly MenuItem _miWord1 = new() { Header = "1 байт", IsCheckable = true, InputGestureText = "Ctrl+1" };
        private readonly MenuItem _miWord2 = new() { Header = "2 байта", IsCheckable = true, InputGestureText = "Ctrl+2" };
        private readonly MenuItem _miWord4 = new() { Header = "4 байта", IsCheckable = true, InputGestureText = "Ctrl+4" };

        private readonly MenuItem _miFillZeros = new() { Header = "Заполнить 0x00" };
        private readonly MenuItem _miFillFFs = new() { Header = "Заполнить 0xFF" };
        private readonly MenuItem _miOpenEditor = new() { Header = "Открыть редактор" };
        private readonly MenuItem _miOpenViewer = new() { Header = "Открыть просмотр" };
        private readonly MenuItem _miEditValue = new() { Header = "Редактировать число…" };
        internal Action<int, int>? EditValue { get; set; }

        private Func<IHexDataSource?> _getData = () => null;
        private Func<int?> _getContextTetradStartIndex = () => null;
        private Func<int?> _getHoveredGroupStartIndex = () => null;
        private Func<int> _getGroupSize = () => 4;

        // Необязательный setter, чтобы сразу записать данные в контрол
        private Action<IList<byte>>? _setData;
        private Action<WordSizeEnum>? _setWordSize;
        private Func<WordSizeEnum>? _getWordSize;
        private Action? _openEditor;
        private Action? _openViewer;
        private Func<string?>? _getCopyText;

        private int? _lastTetradStartIndex;
        private int _lastTetradSize;

        private static readonly (string title, int size)[] _zeroFillSizes =
        {
            ("32 байта", 32),
            ("128 байт", 128),
            ("512 байт", 512),
            ("1 КБ", 1 * 1024),
            ("4 КБ", 4 * 1024),
            ("16 КБ", 16 * 1024),
            ("32 КБ", 32 * 1024),
            ("64 КБ", 64 * 1024),
            ("128 КБ", 128 * 1024),
            ("256 КБ", 256 * 1024),
            ("512 КБ", 512 * 1024),
        };

        public ContextMenuController(FrameworkElement owner)
        {
            _owner = owner;

            _miSaveAll.Click += (_, __) => SaveAll();
            _miCopyAll.Click += (_, __) => CopyAll();
            _miCopyTetrad.Click += (_, __) => CopyTetrad();
            _miLoadFromBin.Click += (_, __) => LoadBin();

            _miWord1.Click += (_, __) => SelectWordSize(WordSizeEnum.Byte1);
            _miWord2.Click += (_, __) => SelectWordSize(WordSizeEnum.Byte2);
            _miWord4.Click += (_, __) => SelectWordSize(WordSizeEnum.Byte4);

            _miOpenEditor.Click += (_, __) => _openEditor?.Invoke();
            _miOpenViewer.Click += (_, __) => _openViewer?.Invoke();
            _miEditValue.Click += (_, _) => { if (_lastTetradStartIndex is int start) EditValue?.Invoke(start, _lastTetradSize); };

            ////todo а в нем много опций, думаю через массивчик сделать (заполнить 00)
            //_miFull.Click += (_, __) => SelectWordSize(WordSizeEnum.Byte1);
            foreach (var (title, size) in _zeroFillSizes)
            {
                var mi = new MenuItem { Header = title };
                mi.Click += (_, __) => FillWithZeros(size);
                _miFillZeros.Items.Add(mi);
            }
            foreach (var (title, size) in _zeroFillSizes)
            {
                var mi = new MenuItem { Header = title };
                mi.Click += (_, __) => FillWithFFs(size);
                _miFillFFs.Items.Add(mi);
            }
        }

        public void SetEditable(bool value)
        {
            _isEditable = value;

            if (_miLoadFromBin != null)
                _miLoadFromBin.IsEnabled = value;

            if (_miOpenEditor != null)
                _miOpenEditor.IsEnabled = value;

            _miFillZeros.IsEnabled = value;
            _miFillFFs.IsEnabled = value;
            _miEditValue.IsEnabled = value && _lastTetradStartIndex.HasValue;
        }

        private void FillWithZeros(int size)
        {
            if (!_isEditable || _setData == null)
                return;

            var bytes = new byte[size]; // по умолчанию всё 0x00
            _setData(bytes);
        }

        private void FillWithFFs(int size)
        {
            if (!_isEditable || _setData == null)
                return;

            var bytes = new byte[size];
            Array.Fill(bytes, (byte)0xFF);
            _setData(bytes);
        }

        /// <summary>
        /// Привязка делегатов доступа к данным и опционального сеттера данных.
        /// </summary>
        public void Bind(
             Func<IHexDataSource?> getData,
             Func<int?> getContextTetradStartIndex,
             Func<int?> getHoveredGroupStartIndex,
             Func<int> getGroupSize,
             Action<IList<byte>>? setData = null,
             Action<WordSizeEnum>? setWordSize = null,
             Func<WordSizeEnum>? getWordSize = null,
             Action? openEditor = null,
             Action? openViewer = null,
             Func<string?>? getCopyText = null)
        {
            _getData = getData;
            _getContextTetradStartIndex = getContextTetradStartIndex;
            _getHoveredGroupStartIndex = getHoveredGroupStartIndex;
            _getGroupSize = getGroupSize;
            _setData = setData;
            _setWordSize = setWordSize;
            _getWordSize = getWordSize;
            _openEditor = openEditor;
            _openViewer = openViewer;
            _getCopyText = getCopyText;
        }

        public void Attach(bool isLoadVisible)
        {
            _menu.Items.Add(_miEditValue);
            _menu.Items.Add(_miOpenEditor);
            _menu.Items.Add(_miOpenViewer);
            _menu.Items.Add(new Separator());

            _menu.Items.Add(_miCopyAll);
            _menu.Items.Add(_miCopyTetrad);
            _menu.Items.Add(new Separator());

            _menu.Items.Add(_miLoadFromBin);
            _menu.Items.Add(_miSaveAll);

            _menu.Items.Add(new Separator());
            _menu.Items.Add(_miWordSize);

            _miWordSize.Items.Add(_miWord1);
            _miWordSize.Items.Add(_miWord2);
            _miWordSize.Items.Add(_miWord4);

            _menu.Items.Add(new Separator());
            _menu.Items.Add(_miFillZeros);
            _menu.Items.Add(_miFillFFs);

            _miLoadFromBin.Visibility = isLoadVisible ? Visibility.Visible : Visibility.Collapsed;
            ContextMenuService.SetContextMenu(_owner, _menu);
        }

        private void SelectWordSize(WordSizeEnum size)
        {
            _miWord1.IsChecked = size == WordSizeEnum.Byte1;
            _miWord2.IsChecked = size == WordSizeEnum.Byte2;
            _miWord4.IsChecked = size == WordSizeEnum.Byte4;
            _setWordSize?.Invoke(size);
        }

        public void SetLoadVisible(bool visible)
        {
            _miLoadFromBin.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void OpenAtMouse(bool hasData, int? tetradStartIndex)
        {
            var ds = _getData();
            int count = ds?.Count ?? 0;

            //заполнение нулями только если данных нет?
            if (hasData)
                _miFillZeros.Header = "Заполнить 0x00 (очищение данных)";
            else
                _miFillZeros.Header = "Заполнить 0x00";
            if (hasData)
                _miFillFFs.Header = "Заполнить 0xFF (очищение данных)";
            else
                _miFillFFs.Header = "Заполнить 0xFF";

            _miSaveAll.IsEnabled = hasData;
            _miOpenEditor.IsEnabled = _isEditable && hasData;
            _miOpenViewer.IsEnabled = hasData;

            _miCopyTetrad.IsEnabled = tetradStartIndex is int s && s >= 0;
            _lastTetradStartIndex = _miCopyTetrad.IsEnabled ? tetradStartIndex : null;
            _lastTetradSize = _miCopyTetrad.IsEnabled ? Math.Max(0, _getGroupSize()) : 0;
            _miEditValue.IsEnabled = _isEditable && _lastTetradStartIndex.HasValue && _lastTetradSize is >= 1 and <= 8;

            //отрубаем возможность копирование в буфер обмена если больше 1мб
            bool allowCopyAll = hasData && count <= MaxClipboardBytes;
            _miCopyAll.IsEnabled = allowCopyAll;
            _miCopyAll.Header = allowCopyAll ? "Копировать в буфер обмена" : $"Копировать в буфер обмена (недоступно, {count / 1024 / 1024} MB)";

            //выбор какой сейчас byte-type
            if (_getWordSize != null) SelectWordSize(_getWordSize());

            _menu.PlacementTarget = _owner;
            _menu.Placement = PlacementMode.MousePoint;
            _menu.IsOpen = true;
        }

        private void CopyAll()
        {
            var ds = _getData();
            if (ds is null || ds.Count == 0) return;

            if (_getCopyText?.Invoke() is { } formatted)
            {
                TrySetClipboardText(formatted);
                return;
            }

            var arr = new byte[ds.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = ds[i];

            string hex = FormatHex.BytesToHexBlockString(arr);
            TrySetClipboardText(hex);
        }

        private void CopyTetrad()
        {
            var ds = _getData();
            if (ds is null || ds.Count == 0) return;
            if (_lastTetradStartIndex is not int start || start < 0) return;

            int len = Math.Min(_lastTetradSize, ds.Count - start);
            if (len <= 0) return;

            var tmp = new byte[len];
            for (int i = 0; i < len; i++)
                tmp[i] = ds[start + i];

            string hex = FormatHex.BytesToHexString(tmp);
            TrySetClipboardText(hex);
        }

        private async void SaveAll()
        {
            var ds = _getData();
            if (ds is null || ds.Count == 0) return;

            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить бинарные данные",
                Filter = "Binary file (*.bin)|*.bin|All files (*.*)|*.*",
                FileName = "data.bin",
                OverwritePrompt = true,
                AddExtension = true,
                DefaultExt = ".bin"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                await using var stream = new FileStream(dlg.FileName, FileMode.Create, FileAccess.Write,
                    FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[Math.Min(81920, ds.Count)];
                for (int offset = 0; offset < ds.Count; offset += buffer.Length)
                {
                    int count = Math.Min(buffer.Length, ds.Count - offset);
                    for (int i = 0; i < count; i++) buffer[i] = ds[offset + i];
                    await stream.WriteAsync(buffer.AsMemory(0, count));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Не удалось сохранить файл:\n{ex.Message}", "Просмотр HEX",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadBin()
        {
            if (!_isEditable) return;
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Загрузить бинарные данные",
                Filter = "Бинарные файлы (*.bin)|*.bin|Все файлы (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (ofd.ShowDialog() != true) return;

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(ofd.FileName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}", "Просмотр HEX",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Если передан setter — сразу кладём данные в контрол
            if (_setData != null)
            {
                _setData(bytes);
            }
            else
            {
                // Иначе можно поднять MessageBox — или кинуть событие наружу.
                // Здесь ничего не делаем, чтобы не зависеть от конкретного HexViewer.
            }
        }

        private static void TrySetClipboardText(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show("Буфер обмена временно недоступен.", "Просмотр HEX",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
