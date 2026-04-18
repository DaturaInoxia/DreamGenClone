namespace DreamGenClone.Domain.StoryAnalysis;

public sealed class StatKeywordCategory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string StatName { get; set; } = string.Empty;

    public int PerKeywordDelta { get; set; } = 1;

    public int MaxAbsDelta { get; set; } = 3;

    public bool IsEnabled { get; set; } = true;

    public int SortOrder { get; set; }

    public List<StatKeywordRule> Keywords { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StatKeywordRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string CategoryId { get; set; } = string.Empty;

    public string Keyword { get; set; } = string.Empty;

    public int SortOrder { get; set; }
}
