namespace DreamGenClone.Application.RolePlay;

public sealed class EncounterSummaryJobPayload
{
    public string SessionId { get; set; } = string.Empty;

    /// <summary>The CycleIndex of the arc this record belongs to.</summary>
    public int CycleIndex { get; set; }

    /// <summary>
    /// The specific EncounterSummaryRecord.Id to enhance.
    /// When set the job enhances exactly this record.
    /// Null is legacy behavior (arc-completion batch by CycleIndex).
    /// </summary>
    public string? SummaryId { get; set; }

    /// <summary>
    /// "PhaseMilestone" or "ArcCompletion". Drives which LLM prompt the handler uses.
    /// </summary>
    public string SummaryType { get; set; } = "ArcCompletion";
}
