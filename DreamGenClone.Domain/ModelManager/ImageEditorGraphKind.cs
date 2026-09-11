namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The ComfyUI graph shape an image-editing model requires. Persisted as text on
/// <see cref="RegisteredModel.ImageEditorGraphKind"/> and selected by the editing client.
/// It is never inferred from artifact names: an editor that needs it but has none configured
/// fails fast during resolution.
/// </summary>
public enum ImageEditorGraphKind
{
    /// <summary>Separate diffusion model, text encoder and VAE artifacts (UNETLoader + CLIPLoader + VAELoader).</summary>
    SplitUnet = 0,

    /// <summary>A single merged checkpoint bundling model+clip+vae, loaded with CheckpointLoaderSimple.</summary>
    MergedCheckpoint = 1
}

/// <summary>Persistence text contract for <see cref="ImageEditorGraphKind"/>.</summary>
public static class ImageEditorGraphKinds
{
    public const string SplitUnet = "SplitUnet";
    public const string MergedCheckpoint = "MergedCheckpoint";

    /// <summary>All persisted values, in UI order.</summary>
    public static IReadOnlyList<string> All { get; } = [SplitUnet, MergedCheckpoint];

    public static string ToPersistedValue(ImageEditorGraphKind kind) => kind switch
    {
        ImageEditorGraphKind.SplitUnet => SplitUnet,
        ImageEditorGraphKind.MergedCheckpoint => MergedCheckpoint,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown image editor graph kind.")
    };

    /// <summary>
    /// Parses a persisted graph-kind value. Blank/absent means "not configured" (null); an unknown
    /// value is a configuration error and throws rather than degrading to a default graph.
    /// </summary>
    public static ImageEditorGraphKind? ParseOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim() switch
        {
            SplitUnet => ImageEditorGraphKind.SplitUnet,
            MergedCheckpoint => ImageEditorGraphKind.MergedCheckpoint,
            _ => throw new InvalidOperationException(
                $"Unknown image editor graph kind '{value}'. Allowed values: {string.Join(", ", All)}.")
        };
    }
}
