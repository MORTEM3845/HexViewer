namespace HexViewer.Items;

public static class ByteSearch
{
    public const int DefaultMatchLimit = 100_000;

    public static List<int> FindMatches(IReadOnlyList<byte> data, IReadOnlyList<byte> pattern,
        int matchLimit = DefaultMatchLimit, CancellationToken cancellationToken = default)
        => FindMatchesFrom(data, pattern, 0, matchLimit, cancellationToken);

    public static List<int> FindMatchesFrom(IReadOnlyList<byte> data, IReadOnlyList<byte> pattern,
        int startIndex, int matchLimit = DefaultMatchLimit, CancellationToken cancellationToken = default)
    {
        var matches = new List<int>(Math.Min(1024, Math.Max(0, matchLimit)));
        if (pattern.Count == 0 || pattern.Count > data.Count || matchLimit <= 0) return matches;

        startIndex = Math.Clamp(startIndex, 0, data.Count);
        if (startIndex > data.Count - pattern.Count) return matches;

        byte first = pattern[0];
        for (int i = startIndex; i <= data.Count - pattern.Count && matches.Count < matchLimit; i++)
        {
            if (((i - startIndex) & 0x3FFF) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (data[i] != first) continue;
            int j = 1;
            while (j < pattern.Count && data[i + j] == pattern[j]) j++;
            if (j == pattern.Count) matches.Add(i);
        }
        return matches;
    }
}
