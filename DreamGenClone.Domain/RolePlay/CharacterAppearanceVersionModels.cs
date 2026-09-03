namespace DreamGenClone.Domain.RolePlay;

public enum CharacterAppearanceVersionStatus
{
    Draft = 1,
    Approved = 2,
    Superseded = 3
}

public sealed class CharacterBodyProfileVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CharacterProfileId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public CharacterAppearanceVersionStatus Status { get; set; } = CharacterAppearanceVersionStatus.Draft;
    public string DescriptorSnapshotJson { get; set; } = "{}";
    public string? SupersedesId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedUtc { get; set; }
}

public sealed class CharacterWardrobeLookVersion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CharacterProfileId { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public CharacterAppearanceVersionStatus Status { get; set; } = CharacterAppearanceVersionStatus.Draft;
    public string DescriptorSnapshotJson { get; set; } = "{}";
    public string CoverageFactsJson { get; set; } = "{}";
    public string? SupersedesId { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedUtc { get; set; }
}

public sealed class CharacterBodyAssetBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BodyProfileVersionId { get; set; } = string.Empty;
    public string SceneAssetId { get; set; } = string.Empty;
    public string SemanticRole { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string CropFactsJson { get; set; } = "{}";
    public string AngleFactsJson { get; set; } = "{}";
    public string BodyCoverageJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CharacterWardrobeAssetBinding
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WardrobeLookVersionId { get; set; } = string.Empty;
    public string SceneAssetId { get; set; } = string.Empty;
    public string SemanticRole { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string GarmentFactsJson { get; set; } = "{}";
    public string ColorFactsJson { get; set; } = "{}";
    public string BodyCoverageJson { get; set; } = "{}";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}