namespace DreamGenClone.Domain.RolePlay;

public sealed class NarrativeGateProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsDefault { get; set; }

    public List<NarrativeGateRule> Rules { get; set; } = [];

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class NarrativeGateRule
{
    public int SortOrder { get; set; }

    public string FromPhase { get; set; } = "Committed";

    public string ToPhase { get; set; } = "Approaching";

    public string MetricKey { get; set; } = NarrativeGateMetricKeys.ActiveScenarioScore;

    public string Comparator { get; set; } = NarrativeGateComparators.GreaterThanOrEqual;

    public decimal Threshold { get; set; }
}

public static class NarrativeGateMetricKeys
{
    public const string ActiveScenarioScore = "ActiveScenarioScore";
    public const string AverageDesire = "AverageDesire";
    public const string AverageRestraint = "AverageRestraint";
    public const string AverageTension = "AverageTension";
    public const string AverageConnection = "AverageConnection";
    public const string AverageDominance = "AverageDominance";
    public const string AverageLoyalty = "AverageLoyalty";
    public const string AverageSelfRespect = "AverageSelfRespect";
    public const string InteractionsSinceCommitment = "InteractionsSinceCommitment";
}

public static class NarrativeGateComparators
{
    public const string GreaterThanOrEqual = ">=";
    public const string GreaterThan = ">";
    public const string LessThanOrEqual = "<=";
    public const string LessThan = "<";
    public const string Equal = "==";
}
