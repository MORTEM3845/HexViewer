namespace HexViewer.Control.Services
{
    internal class MathHelper
    {
        public bool IsTetradValid(IList<byte> data, int startIndex)
        {
            // Если есть хотя бы 1 байт — ок
            return data != null && startIndex < data.Count;
        }
    }
}
