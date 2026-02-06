namespace HexViewer.Control.Services
{
    public sealed class ViewportController
    {
        public int FirstRow { get; private set; }
        public int VisibleRows { get; set; }

        public int TotalRows(int count, int columns) => count == 0 ? 0 : (int)Math.Ceiling(count / (double)columns);

        public bool SetFirstRow(int value, int totalRows)
        {
            int max = Math.Max(0, totalRows - VisibleRows);
            int nv = Math.Clamp(value, 0, max);
            if (nv == FirstRow) return false;
            FirstRow = nv;
            return true;
        }
    }
}
