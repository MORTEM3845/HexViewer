using System.Text;
using System.Windows;
using HexViewer;

namespace HexViewerDemo;

public partial class MainWindow : Window
{
    private byte[] _data = CreateData();

    public MainWindow()
    {
        InitializeComponent();
        Preview.DataChangedExternally += (_, bytes) => LoadData(bytes.ToArray());
        LoadData(_data);
    }

    private void LoadData(byte[] data)
    {
        _data = data;
        Preview.Data = data;
        InlineEditor.SetBytes(data.Take(64).ToArray());
        WordPreview.Data = data.Take(8).ToArray();
    }

    private void OpenViewer_Click(object sender, RoutedEventArgs e) =>
        new HexViewerWindow(_data) { Owner = this }.Show();

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        var editor = new HexEditorWindow(_data) { Owner = this };
        editor.ShowDialog();
        if (editor.IsSaved) LoadData(editor.GetBytes());
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => LoadData(CreateData());

    private static byte[] CreateData()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        Encoding.ASCII.GetBytes("HexViewer WPF | Binary data demo").CopyTo(data, 0);
        return data;
    }
}
