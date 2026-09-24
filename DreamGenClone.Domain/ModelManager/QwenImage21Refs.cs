namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The Qwen-Image-2.1 artifacts and envelope a generation request needs, read from the model's
/// <c>CapabilityQualificationsJson</c> (the <c>NativeMultiReference</c> entry) at resolution time.
/// </summary>
/// <remarks>
/// 2.1 is a split model - a 7B DiT, a Qwen3-VL-8B text encoder and an RGBA VAE - behind ONE
/// <c>TextEncodeQwenImage21</c> node that returns positive, negative AND the latent, and that takes
/// reference images through an autogrow input. None of those artifact names is derivable from the
/// model identifier, so they are carried explicitly and every one of them is REQUIRED: there is no
/// default filename anywhere in the graph builder.
/// </remarks>
/// <param name="UnetName">DiT / diffusion model filename (<c>UNETLoader.unet_name</c>).</param>
/// <param name="TextEncoderName">Text encoder filename (<c>CLIPLoader.clip_name</c>, type <c>qwen_image</c>).</param>
/// <param name="VaeName">VAE filename (<c>VAELoader.vae_name</c>).</param>
/// <param name="ResolutionBudget">
/// <c>TextEncodeQwenImage21.resolution</c> - a total pixel BUDGET, not a dimension. Reference images
/// are resized to about budget x budget at multiples of 32, preserving aspect ratio.
/// </param>
/// <param name="MaxReferences">
/// Highest accepted reference count. 2.1 advertises sixteen autogrow slots; the published templates
/// name ten. Requests above this are rejected rather than truncated.
/// </param>
/// <param name="Steps">Sampler steps for the 2.1 graph (published examples: 25-50).</param>
/// <param name="Cfg">
/// Classifier-free guidance for the 2.1 graph. The published/official value is <b>1.0</b>, which is what
/// makes the negative prompt inert. This is a MODEL property, not a studio preference: applying an
/// SDXL-style cfg (for example 5) drives a cfg-1-distilled model far out of distribution and produces
/// blown-out, grainy images (observed 2026-09-24).
/// </param>
/// <param name="SamplerName">Sampler (<c>KSampler.sampler_name</c>); published examples use <c>euler</c>.</param>
/// <param name="Scheduler">Scheduler (<c>KSampler.scheduler</c>); published examples use <c>simple</c>.</param>
public sealed record QwenImage21Refs(
    string UnetName,
    string TextEncoderName,
    string VaeName,
    int ResolutionBudget,
    int MaxReferences,
    int Steps,
    double Cfg,
    string SamplerName,
    string Scheduler);
