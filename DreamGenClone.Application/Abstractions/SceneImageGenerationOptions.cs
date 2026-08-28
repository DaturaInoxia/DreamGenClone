namespace DreamGenClone.Application.Abstractions;

/// <summary>
/// Optional ComfyUI sampler/CLIP overrides for a scene-image render. Null (or null fields) mean
/// "use the model-family default recipe". Applied by <see cref="IImageGenerationClient"/> to the
/// ComfyUI workflow; ignored by non-ComfyUI protocols.
/// </summary>
public sealed record SceneImageGenerationOptions
{
    /// <summary>CFG scale, e.g. 6.5.</summary>
    public double? Cfg { get; init; }

    /// <summary>Sampling step count, e.g. 30.</summary>
    public int? Steps { get; init; }

    /// <summary>Sampler name, e.g. "dpmpp_2m".</summary>
    public string? SamplerName { get; init; }

    /// <summary>Scheduler name, e.g. "karras".</summary>
    public string? Scheduler { get; init; }

    /// <summary>CLIP skip layer (e.g. -2 for skip 2). Null = no CLIPSetLastLayer node.</summary>
    public int? ClipSkip { get; init; }
}
