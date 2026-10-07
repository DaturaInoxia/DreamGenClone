namespace DreamGenClone.Domain.RolePlay;

public enum ReferenceBootstrapBatchStatus
{
    Draft = 1,
    Generating = 2,
    Complete = 3,
    Failed = 4
}

public sealed record ReferenceBootstrapBatch
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? CharacterProfileId { get; init; }
    public SceneAssetType? TargetAssetType { get; init; }
    public string Description { get; init; } = string.Empty;
    public string? FrozenTextBlock { get; init; }
    public int RequestedCandidateCount { get; init; }
    public ReferenceBootstrapBatchStatus Status { get; init; } = ReferenceBootstrapBatchStatus.Draft;
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; init; } = DateTime.UtcNow;
}