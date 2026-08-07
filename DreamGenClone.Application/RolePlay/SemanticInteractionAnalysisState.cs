namespace DreamGenClone.Application.RolePlay;

public sealed class SemanticInteractionAnalysisState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string SessionId { get; set; } = string.Empty;

    public string InteractionId { get; set; } = string.Empty;

    public string CharacterId { get; set; } = string.Empty;

    public SemanticAnalysisStatus Status { get; set; } = SemanticAnalysisStatus.Idle;

    public string? ErrorMessage { get; set; }

    public string? ResultJson { get; set; }

    public string? PromptSystem { get; set; }

    public string? PromptUser { get; set; }

    public string? RawModelOutput { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public DateTime? AnalyzedUtc { get; set; }
}
