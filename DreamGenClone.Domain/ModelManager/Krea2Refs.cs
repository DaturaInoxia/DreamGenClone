namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The Krea 2 artifacts and sampling envelope a generation request needs, read from the model's
/// <c>CapabilityQualificationsJson</c> at resolution time.
/// </summary>
/// <remarks>
/// Krea 2 is a SPLIT model - a 12B dense diffusion transformer, a Qwen3-VL 4B text encoder and the
/// Qwen Image VAE - behind three separate loader nodes. None of those artifact names is derivable from
/// the model identifier, so they are carried explicitly and every one of them is REQUIRED: there is no
/// default filename anywhere in the graph builder.
///
/// <para>
/// The envelope is a MODEL property, not a studio preference. Krea-2 Turbo is a cfg-1 distilled model
/// (the negative is a <c>ConditioningZeroOut</c> of the positive, so a negative prompt is inert), sampled
/// with euler/simple at 8 steps and denoise 1. Applying an SDXL-style cfg (5) or an SDXL sampler
/// (dpmpp_2m_sde/karras) drives it far out of distribution - the same class of failure the 2.1
/// blown-out render of 2026-09-24 came from, which is why these values are qualified per model here
/// rather than read from the studio controls.
/// </para>
///
/// <para>
/// <b>No size and no base LoRA stack.</b> The render's canvas comes from the request's own size (the single
/// source of truth for every family), and no LoRA is applied by configuration: the scene-LoRA set is the
/// OPERATOR's multi-select, resolved from the <c>SceneLora</c> catalog, so a model row cannot force a LoRA
/// onto a render. See the B-137 plan §4.
/// </para>
/// </remarks>
/// <param name="UnetName">Diffusion model filename (<c>UNETLoader.unet_name</c>).</param>
/// <param name="ClipName">Text encoder filename (<c>CLIPLoader.clip_name</c>, type <c>krea2</c>).</param>
/// <param name="VaeName">VAE filename (<c>VAELoader.vae_name</c>).</param>
/// <param name="Steps">Sampler steps (Krea-2 Turbo is qualified at 8).</param>
/// <param name="Cfg">Classifier-free guidance (Krea-2 Turbo is qualified at 1, where the negative is inert).</param>
/// <param name="SamplerName">Sampler (<c>KSampler.sampler_name</c>); the qualified value is <c>euler</c>.</param>
/// <param name="Scheduler">Scheduler (<c>KSampler.scheduler</c>); the qualified value is <c>simple</c>.</param>
/// <param name="Denoise">Denoise strength; the qualified value is 1 (a full text-to-image render).</param>
public sealed record Krea2Refs(
    string UnetName,
    string ClipName,
    string VaeName,
    int Steps,
    double Cfg,
    string SamplerName,
    string Scheduler,
    double Denoise);
