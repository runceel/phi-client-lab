using System.Collections.Immutable;

namespace PhiClient.Core;

public sealed record LogSearchResult(ImmutableArray<long> Hits, long? Current)
{
    public int Position => Current is { } id ? Hits.IndexOf(id) + 1 : 0;

    public LogSearchResult Step(int direction)
    {
        if (Hits.IsEmpty) return this;
        var index = Hits.IndexOf(Current ?? -1);
        return this with { Current = Hits[(Math.Max(index, 0) + direction % Hits.Length + Hits.Length) % Hits.Length] };
    }
}

public static class LogSearch
{
    public static LogSearchResult Find(IEnumerable<(long Id, string Text)> rows, string query, long? current = null)
    {
        var hits = string.IsNullOrEmpty(query) ? [] : rows
            .Where(row => row.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Id).ToImmutableArray();
        return new(hits, current is { } id && hits.Contains(id) ? id : hits.IsEmpty ? null : hits[^1]);
    }
}
