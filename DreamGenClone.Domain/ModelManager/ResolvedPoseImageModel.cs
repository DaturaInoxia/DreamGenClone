namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// Immutable value object describing how to call the pose-conditioned (ControlNet OpenPose) image
/// path, resolved from the Model Manager at call time. Mirrors <see cref="ResolvedIdentityImageModel"/>
/// for the pose/ControlNet render path. Every field is a configured value; the resolver never
/// substitutes a default.
/// </summary>
public sealed record ResolvedPoseImageModel(
    string ProviderBaseUrl,
    int ProviderTimeoutSeconds,
    string ModelIdentifier,
    ImageContentPolicy ContentPolicy,
    string ProviderName,
    /// <summary>ComfyUI ControlNet weight path as ComfyUI lists it (e.g. Windows
    /// "thibaud-openpose-xl2\OpenPoseXL2.safetensors"). Configured per model — never hardcoded.</summary>
    string ControlNetAdapterRef,
    /// <summary>Configured default conditioning strength for this model's ControlNet (0 &lt; strength &lt;= 1).</summary>
    double DefaultStrength,
    string? ApiKeyEncrypted = null,
    ImageProtocol ImageProtocol = ImageProtocol.ComfyUi,
    /// <summary>
    /// Which graph the client must build. SDXL and FLUX are different GRAPHS, not one graph with a different
    /// checkpoint — FLUX loads a UNET, two text encoders and a VAE separately and samples with
    /// <c>XlabsSampler</c> — so this decides the builder and nothing else.
    /// </summary>
    SceneImageModelFamily Family = SceneImageModelFamily.Sdxl,
    /// <summary>
    /// The XLabs FLUX OpenPose references. Populated for <see cref="SceneImageModelFamily.Flux"/> and null for SDXL,
    /// whose graph needs only the checkpoint and the ControlNet weight.
    /// </summary>
    FluxPoseRefs? Flux = null);

/// <summary>
/// The references the XLabs FLUX OpenPose graph needs beyond the checkpoint and the ControlNet weight. They are
/// separate files on the ComfyUI host, so each one is a CONFIGURED Model Manager value rather than an app default:
/// the graph cannot run without them, and a substituted encoder or VAE would render a silently different picture
/// instead of failing.
/// </summary>
public sealed record FluxPoseRefs(
    /// <summary>UNET name under <c>models/diffusion_models</c>: the local host serves FLUX as a UNET, not a checkpoint.</summary>
    string UnetName,
    /// <summary>First <c>DualCLIPLoader</c> text encoder (t5xxl).</summary>
    string ClipName1,
    /// <summary>Second <c>DualCLIPLoader</c> text encoder (clip_l).</summary>
    string ClipName2,
    /// <summary>VAE name under <c>models/vae</c>.</summary>
    string VaeName,
    /// <summary><c>LoadFluxControlNet.model_name</c>: which FLUX variant the ControlNet was built for.</summary>
    string ControlNetModelName,
    /// <summary><c>FluxGuidance</c> and <c>XlabsSampler.true_gs</c> — the FLUX guidance scale.</summary>
    double Guidance,
    /// <summary>Sampler steps.</summary>
    int Steps,
    /// <summary><c>XlabsSampler.timestep_to_start_cfg</c>.</summary>
    int TimestepToStartCfg);
