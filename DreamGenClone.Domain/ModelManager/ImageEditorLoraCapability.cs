namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// What a source-image editor model's configured editor LoRA is FOR, persisted on
/// <see cref="RegisteredModel.ImageEditorLoraCapability"/> and read by the edit workspace so a
/// capability-gated control (e.g. the multi-angle camera tab) can offer exactly the models whose
/// LoRA can execute it.
///
/// <para>
/// The flag names a CAPABILITY, never an artifact: the actual LoRA filename stays in
/// <see cref="RegisteredModel.ImageEditorLoraName"/>. A capability that requires a LoRA must have one
/// configured or resolution fails fast — a capability is not a fallback.
/// </para>
/// </summary>
public enum ImageEditorLoraCapability
{
    /// <summary>No editor LoRA, or a generic edit LoRA with no named capability.</summary>
    None = 0,

    /// <summary>
    /// The editor LoRA is a multi-angle camera control LoRA (the fal
    /// Qwen-Image-Edit-2511 Multiple-Angles LoRA). It responds to the <c>&lt;sks&gt; azimuth elevation
    /// distance</c> grammar and orbits a single isolated subject.
    /// </summary>
    MultiAngleCamera = 1
}

/// <summary>Persistence text contract for <see cref="ImageEditorLoraCapability"/>.</summary>
public static class ImageEditorLoraCapabilities
{
    public const string None = "None";
    public const string MultiAngleCamera = "MultiAngleCamera";

    /// <summary>All persisted values, in UI order.</summary>
    public static IReadOnlyList<string> All { get; } = [None, MultiAngleCamera];

    public static string ToPersistedValue(ImageEditorLoraCapability capability) => capability switch
    {
        ImageEditorLoraCapability.None => None,
        ImageEditorLoraCapability.MultiAngleCamera => MultiAngleCamera,
        _ => throw new ArgumentOutOfRangeException(nameof(capability), capability, "Unknown image editor LoRA capability.")
    };

    /// <summary>
    /// Parses a persisted capability value. Blank/absent means <see cref="ImageEditorLoraCapability.None"/>;
    /// an unknown value is a configuration error and throws rather than degrading to "no capability".
    /// </summary>
    public static ImageEditorLoraCapability ParseOrNone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return ImageEditorLoraCapability.None;

        return value.Trim() switch
        {
            None => ImageEditorLoraCapability.None,
            MultiAngleCamera => ImageEditorLoraCapability.MultiAngleCamera,
            _ => throw new InvalidOperationException(
                $"Unknown image editor LoRA capability '{value}'. Allowed values: {string.Join(", ", All)}.")
        };
    }
}
