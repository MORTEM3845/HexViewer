using HexViewer.Enums;
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
    public interface IHexDataSource
    {
        int Count { get; }
        byte this[int index] { get; }
    }

    // Адаптер данных (без интерфейсов)
    public sealed class ListHexDataSource : IHexDataSource
    {
        private readonly IList<byte> _data;
        public ListHexDataSource(IList<byte> data) => _data = data;
        public int Count => _data.Count;
        public byte this[int index] => _data[index];
        public IList<byte> Raw => _data;
    }

    /// <summary>
    /// Контроллер контекстного меню без DI/интерфейсов.
    /// Работает напрямую с Clipboard и Win32 диалогами.
    /// </summary>
    public sealed class ContextMenuController
    {
        private readonly FrameworkElement _owner;
        private const int MaxClipboardBytes = 1_000_000;

        private readonly ContextMenu _menu = new();
        private readonly MenuItem _miSaveAll = new() { Header = "Сохранить как бинарный файл" };
        private readonly MenuItem _miCopyAll = new() { Header = "Копировать в буфер обмена" };
        private readonly MenuItem _miCopyTetrad = new() { Header = "Копировать выделенное в буфер обмена" };
        private readonly MenuItem _miLoadFromBin = new() { Header = "Загрузить из бинарного файла" };

        private readonly MenuItem _miWordSize = new() { Header = "Размер слова" };
        private readonly MenuItem _miWord1 = new() { Header = "1 байт", IsCheckable = true };
        private readonly MenuItem _miWord2 = new() { Header = "2 байта", IsCheckable = true };
        private readonly MenuItem _miWord4 = new() { Header = "4 байта", IsCheckable = true };

        private readonly MenuItem _miFillZeros = new() { Header = "Заполнить нулями" };

        private Func<IHexDataSource?> _getData = () => null;
        private Func<int?> _getContextTetradStartIndex = () => null;
        private Func<int?> _getHoveredGroupStartIndex = () => null;
        private Func<int> _getGroupSize = () => 4;

        // Необязательный setter, чтобы сразу записать данные в контрол
        private Action<IList<byte>>? _setData;
        private Action<WordSizeEnum>? _setWordSize;
        private Func<WordSizeEnum>? _getWordSize;

        private int? _lastTetradStartIndex;

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

            ////todo а в нем много опций, думаю через массивчик сделать (заполнить 00)
            //_miFull.Click += (_, __) => SelectWordSize(WordSizeEnum.Byte1);
            foreach (var (title, size) in _zeroFillSizes)
            {
                var mi = new MenuItem { Header = title };
                mi.Click += (_, __) => FillWithZeros(size);
                _miFillZeros.Items.Add(mi);
            }
        }

        private void FillWithZeros(int size)
        {
            if (_setData == null)
                return;

            var bytes = new byte[size]; // по умолчанию всё 0x00
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
                 Func<WordSizeEnum>? getWordSize = null)
        {
            _getData = getData;
            _getContextTetradStartIndex = getContextTetradStartIndex;
            _getHoveredGroupStartIndex = getHoveredGroupStartIndex;
            _getGroupSize = getGroupSize;
            _setData = setData;
            _setWordSize = setWordSize;
            _getWordSize = getWordSize;
        }

        public void Attach(bool isLoadVisible)
        {
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
                _miFillZeros.Header = "Заполнить нулями (очищение данных)";
            else
                _miFillZeros.Header = "Заполнить нулями";

            _miSaveAll.IsEnabled = hasData;

            _miCopyTetrad.IsEnabled = tetradStartIndex is int s && s >= 0;
            _lastTetradStartIndex = _miCopyTetrad.IsEnabled ? tetradStartIndex : null;

            //отрубаем возможность копирование в буфер обмена если больше 1мб
            bool allowCopyAll = hasData && count <= MaxClipboardBytes;
            _miCopyAll.IsEnabled = allowCopyAll;
            _miCopyAll.Header = allowCopyAll? "Копировать в буфер обмена" : $"Копировать в буфер обмена (недоступно, {count / 1024 / 1024} MB)";

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

            var arr = new byte[ds.Count];
            for (int i = 0; i < arr.Length; i++) arr[i] = ds[i];

            string hex = FormatHex.BytesToHexBlockString(arr);
            Clipboard.SetText(hex);
        }

        private void CopyTetrad()
        {
            var ds = _getData();
            if (ds is null || ds.Count == 0) return;
            if (_lastTetradStartIndex is not int start || start < 0) return;

            int len = Math.Min(_getGroupSize(), ds.Count - start);
            if (len <= 0) return;

            var tmp = new byte[len];
            for (int i = 0; i < len; i++)
                tmp[i] = ds[start + i];

            string hex = FormatHex.BytesToHexString(tmp);
            Clipboard.SetText(hex);
        }

        private void SaveAll()
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

            var tmp = new byte[ds.Count];
            for (int i = 0; i < tmp.Length; i++) tmp[i] = ds[i];

            File.WriteAllBytes(dlg.FileName, tmp);
        }

        private void LoadBin()
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Загрузить бинарные данные",
                Filter = "Бинарные файлы (*.bin)|*.bin|Все файлы (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (ofd.ShowDialog() != true) return;

            byte[] bytes = File.ReadAllBytes(ofd.FileName);

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
    }
}
