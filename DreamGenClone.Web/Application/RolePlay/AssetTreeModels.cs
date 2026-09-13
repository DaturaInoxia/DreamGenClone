namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// One unified row in the Asset Manager tree, projected from the identity store, the library, or
/// location references. Source ids from different stores never collide because the row carries its
/// <see cref="SourceStore"/> alongside <see cref="AssetId"/>.
/// </summary>
public sealed record AssetTreeItem
{
    /// <summary>"Identity" | "Library" | "Location"</summary>
    public string SourceStore { get; init; } = string.Empty;

    public string AssetId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>Human label: Face / Full-body / Wardrobe / Location / Prop / Style / …</summary>
    public string KindLabel { get; init; } = string.Empty;

    /// <summary>SceneAssetType-mapped value used by the type filter.</summary>
    public string KindFilter { get; init; } = string.Empty;

    /// <summary>"Unreviewed" / "Approved" / "Draft" / "Superseded" / "Revoked" — used by the approval filter.</summary>
    public string ApprovalFilter { get; init; } = "Unreviewed";

    /// <summary>Canonical slot or fine-grained view label (e.g. "Front", "Unclothed Front", "looking down 30°").</summary>
    public string? ViewKey { get; init; }

    public string? FileRelativePath { get; init; }

    public string? StatusLabel { get; init; }

    public DateTime CreatedUtc { get; init; }

    public string? CharacterProfileId { get; init; }

    public string? IdentityPackId { get; init; }

    public string? CandidateBatchId { get; init; }

    /// <summary>Detail link for the row.</summary>
    public string? Href { get; init; }
}

/// <summary>A collapsible group in the tree. A group may nest one level (pack → view set).</summary>
public sealed record AssetTreeGroup
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    /// <summary>Optional badge text (e.g. pack version/status).</summary>
    public string? Badge { get; init; }

    public IReadOnlyList<AssetTreeGroup> Children { get; init; } = [];

    public IReadOnlyList<AssetTreeItem> Items { get; init; } = [];
}

public sealed record AssetTreeOwner
{
    /// <summary>"Character" | "Location" | "Cleanup"</summary>
    public string RootKind { get; init; } = string.Empty;

    public string OwnerId { get; init; } = string.Empty;

    public string OwnerName { get; init; } = string.Empty;

    public string? Badge { get; init; }

    /// <summary>Studio page link for this owner (null = non-navigable bucket).</summary>
    public string? Href { get; init; }
}

/// <summary>A top-level root in the grouped Asset Manager tree.</summary>
public sealed record AssetTreeRoot
{
    public AssetTreeOwner Owner { get; init; } = new();

    public IReadOnlyList<AssetTreeGroup> Groups { get; init; } = [];
}
