using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class ReferenceApplicationSelection
{
    public string ElementKey { get; set; } = string.Empty;
    public string SemanticRole { get; set; } = string.Empty;
    public SceneAssetType AssetType { get; set; }
    public string? ActorKey { get; set; }
    public string Strategy { get; set; } = "TextOnly";
    public decimal? Strength { get; set; } = 1m;
    public string? SceneAssetId { get; set; }
    public string? SceneAssetImageId { get; set; }
    public int? SceneAssetVersion { get; set; }
    public string? SceneAssetSha256 { get; set; }
    public string BindingSnapshotJson { get; set; } = "{}";

    public bool UsesReference => !string.IsNullOrWhiteSpace(SceneAssetId)
        && !string.IsNullOrWhiteSpace(SceneAssetImageId);
}