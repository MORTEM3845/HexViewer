namespace HexViewer.Control.Services;

public interface IHexDataSource
{
    int Count { get; }
    byte this[int index] { get; }
}

public sealed class ListHexDataSource : IHexDataSource
{
    private readonly IList<byte> _data;
    public ListHexDataSource(IList<byte> data) => _data = data;
    public int Count => _data.Count;
    public byte this[int index] => _data[index];
    public IList<byte> Raw => _data;
}
