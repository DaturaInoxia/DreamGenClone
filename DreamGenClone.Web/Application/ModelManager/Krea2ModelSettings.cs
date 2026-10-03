using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>
/// Reads the Krea 2 artifacts and sampling envelope out of a registered model's
/// <c>CapabilityQualificationsJson</c>.
/// </summary>
/// <remarks>
/// Strict and no-fallback, mirroring <see cref="QwenImage21ModelSettings"/>: every value is a configured Model
/// Manager field, and a missing one fails fast naming the exact field. The Krea 2 graph loads a diffusion
/// transformer, a Qwen3-VL text encoder and the Qwen Image VAE, and runs a cfg-1 distilled 8-step recipe, so
/// nothing here may be defaulted or guessed - an SDXL envelope (cfg 5, dpmpp_2m_sde/karras) on this model is a
/// blown-out render, and a guessed artifact name is a 400 from ComfyUI.
/// </remarks>
public static class Krea2ModelSettings
{
    /// <summary>
    /// The qualification a Krea 2 text-to-image model must declare and qualify. It is deliberately NOT a reference
    /// strategy: Krea 2 has no reference conditioning, no edit path and no ControlNet, so the entry carries only
    /// the artifacts and the sampler envelope the generation graph needs.
    /// </summary>
    public const string TextToImageStrategy = "TextToImage";

    public static Krea2Refs Resolve(RegisteredModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var qualification = FindQualification(model);

        if (qualification.Steps is not { } steps || steps < 1)
        {
            throw new ModelResolutionException(
                $"Krea 2 'Steps' must be at least 1 (Krea-2 Turbo is qualified at 8), but model "
                + $"'{model.DisplayName}' has {qualification.Steps?.ToString() ?? "none"} in its "
                + $"'{TextToImageStrategy}' qualification. Set 'Steps' in Model Manager (/model-manager).");
        }

        if (qualification.Cfg is not { } cfg || !double.IsFinite(cfg) || cfg <= 0)
        {
            throw new ModelResolutionException(
                $"Krea 2 'Cfg' must be a positive number (Krea-2 Turbo is qualified at 1, where the negative is "
                + $"inert), but model '{model.DisplayName}' has {qualification.Cfg?.ToString() ?? "none"} in its "
                + $"'{TextToImageStrategy}' qualification. Set 'Cfg' in Model Manager (/model-manager). Do NOT copy "
                + "an SDXL cfg such as 5 here.");
        }

        if (qualification.Denoise is not { } denoise || !double.IsFinite(denoise) || denoise is <= 0 or > 1)
        {
            throw new ModelResolutionException(
                $"Krea 2 'Denoise' must be greater than 0 and at most 1 (a text-to-image render is qualified at 1), "
                + $"but model '{model.DisplayName}' has {qualification.Denoise?.ToString() ?? "none"} in its "
                + $"'{TextToImageStrategy}' qualification. Set 'Denoise' in Model Manager (/model-manager).");
        }

        return new Krea2Refs(
            UnetName: Require(qualification.UnetName, "UnetName", model),
            ClipName: Require(qualification.ClipName, "ClipName", model),
            VaeName: Require(qualification.VaeName, "VaeName", model),
            Steps: steps,
            Cfg: cfg,
            SamplerName: Require(qualification.SamplerName, "SamplerName", model),
            Scheduler: Require(qualification.Scheduler, "Scheduler", model),
            Denoise: denoise);
    }

    private static string Require(string? value, string field, RegisteredModel model) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ModelResolutionException(
                $"Krea 2 qualification is missing '{field}' for model '{model.DisplayName}'. "
                + "Add it to the model's CapabilityQualificationsJson in Model Manager (/model-manager).")
            : value.Trim();

    /// <summary>The model's passing text-to-image qualification, or a fail-fast diagnostic.</summary>
    private static Qualification FindQualification(RegisteredModel model)
    {
        var qualifications = ParseQualifications(model.CapabilityQualificationsJson);
        var qualification = qualifications.FirstOrDefault(entry =>
            string.Equals(entry.Strategy, TextToImageStrategy, StringComparison.OrdinalIgnoreCase)
            && entry.Qualified
            && !string.IsNullOrWhiteSpace(entry.ProofId));

        return qualification
            ?? throw new ModelResolutionException(
                $"Krea 2 model '{model.DisplayName}' has no passing '{TextToImageStrategy}' qualification. Add a "
                + $"qualified '{TextToImageStrategy}' entry to CapabilityQualificationsJson in Model Manager "
                + "(/model-manager): the Krea 2 graph loads a diffusion model, a text encoder and a VAE, and runs a "
                + "cfg-1 distilled 8-step recipe, and cannot run without each one named.");
    }

    private static List<Qualification> ParseQualifications(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<Qualification>>(
                       string.IsNullOrWhiteSpace(json) ? "[]" : json,
                       SerializerOptions)
                   ?? [];
        }
        catch (JsonException exception)
        {
            throw new ModelResolutionException(
                $"CapabilityQualificationsJson is not valid JSON: {exception.Message}");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// The subset of a capability qualification entry Krea 2 needs. Unknown members of an entry are ignored, which
    /// lets one array carry different fields per strategy (as the Qwen NativeMultiReference and FLUX pose entries do).
    /// </summary>
    private sealed class Qualification
    {
        [JsonPropertyName("Strategy")] public string Strategy { get; set; } = string.Empty;
        [JsonPropertyName("Qualified")] public bool Qualified { get; set; }
        [JsonPropertyName("ProofId")] public string? ProofId { get; set; }
        [JsonPropertyName("UnetName")] public string? UnetName { get; set; }
        [JsonPropertyName("ClipName")] public string? ClipName { get; set; }
        [JsonPropertyName("VaeName")] public string? VaeName { get; set; }
        [JsonPropertyName("Steps")] public int? Steps { get; set; }
        [JsonPropertyName("Cfg")] public double? Cfg { get; set; }
        [JsonPropertyName("SamplerName")] public string? SamplerName { get; set; }
        [JsonPropertyName("Scheduler")] public string? Scheduler { get; set; }
        [JsonPropertyName("Denoise")] public double? Denoise { get; set; }
    }
}
