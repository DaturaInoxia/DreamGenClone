namespace DreamGenClone.Domain.RolePlay;

public enum ProducedImageStatus
{
    Undecided = 0,
    Shortlisted = 1,
    Accepted = 2,
    Rejected = 3,
    Revoked = 4
}

public enum ProducedImageKind
{
    ReferenceCandidate = 1,
    EditAttempt = 2,
    MomentImage = 3,
    PromotedReference = 4
}

public enum ProducedImageReferenceKind
{
    CharacterFace = 1,
    CharacterBody = 2,
    Wardrobe = 3,
    Location = 4
}

public enum ProducedImageVisionSource
{
    Typed = 1,
    BeatMetadata = 2,
    SystemData = 3
}

public sealed class ProducedImage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ProducedImageKind Kind { get; set; }
    public string? SessionId { get; set; }
    public string? InteractionId { get; set; }
    public string? BatchId { get; set; }
    public string? TargetRef { get; set; }
    public ProducedImageReferenceKind? ReferenceKind { get; set; }
    public ProducedImageStatus Status { get; set; } = ProducedImageStatus.Undecided;
    public string? ParentImageId { get; set; }
    public ProducedImageVisionSource VisionSource { get; set; }
    public string? VisionText { get; set; }
    public string? PromptCompiled { get; set; }
    public string? PromptEdited { get; set; }
    public string? NegativePrompt { get; set; }
    public long? Seed { get; set; }
    public string? ModelId { get; set; }
    public string? EndpointId { get; set; }
    public string? AppliedReferencesJson { get; set; }
    public string? IdentityStrategy { get; set; }
    public string? CostJson { get; set; }
    public string? StoragePath { get; set; }
    public SceneImageRefusalMode RefusalMode { get; set; } = SceneImageRefusalMode.None;
    public string? ScoreJson { get; set; }
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public string? CandidateNotes { get; set; }
}
