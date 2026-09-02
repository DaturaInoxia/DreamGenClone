namespace DreamGenClone.CorpusRunner;

public static class BenchmarkStatistics
{
    public static long NearestRankPercentile(IEnumerable<long> values, int percentile)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (percentile is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentile));

        var ordered = values.Order().ToArray();
        if (ordered.Length == 0)
            throw new ArgumentException("At least one value is required.", nameof(values));

        var rank = (int)Math.Ceiling(percentile / 100d * ordered.Length);
        return ordered[rank - 1];
    }
}