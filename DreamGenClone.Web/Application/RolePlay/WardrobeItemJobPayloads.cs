namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Payload for one wardrobe-item prompt compilation + render: the operator's short description of the garment, and
/// the model the reference prompt must be geared to.
/// </summary>
public sealed class WardrobeItemPromptJobPayload
{
    public string AssetId { get; set; } = string.Empty;

    /// <summary>
    /// The image row this item's picture will occupy. Created BEFORE the job runs, so the operator sees the attempt the
    /// moment they ask for it and a failure has somewhere to be reported.
    /// </summary>
    public string ImageId { get; set; } = string.Empty;

    /// <summary>The operator's own words ("a yellow sundress"). The compiler expands it; nothing rewrites it first.</summary>
    public string ItemDescription { get; set; } = string.Empty;

    public string ModelId { get; set; } = string.Empty;

    public string ImageSize { get; set; } = string.Empty;

    /// <summary>
    /// Whether the compiled row is rendered as soon as its prompt exists, or held so the operator can read and correct
    /// the prompt first. Data rather than a second job type, because both cases are the same compile followed by a
    /// decision - and the row is the thing that carries the state either way.
    /// </summary>
    public bool RenderWhenCompiled { get; set; } = true;
}
