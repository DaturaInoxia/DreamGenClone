namespace DreamGenClone.Domain.RolePlay;

public sealed class ScenarioCandidateEvaluation
{
    public string SessionId { get; set; } = string.Empty;
    public string EvaluationId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string StageAWillingnessTier { get; set; } = string.Empty;
    public bool StageBEligible { get; set; }
    public decimal FitScore { get; set; }
    public decimal Confidence { get; set; }
    public string TieBreakKey { get; set; } = string.Empty;
    public string Rationale { get; set; } = string.Empty;
    public DateTime EvaluatedUtc { get; set; } = DateTime.UtcNow;
}
