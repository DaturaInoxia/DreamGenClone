using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>
/// Reads the Qwen-Image-2.1 artifacts and envelope out of a registered model's
/// <c>CapabilityQualificationsJson</c>.
/// </summary>
/// <remarks>
/// Strict and no-fallback, mirroring <see cref="PoseImageModelResolver"/> for FLUX: every value is a
/// configured Model Manager field, and a missing one fails fast naming the exact field. The 2.1 graph
/// loads a DiT, a Qwen3-VL text encoder and an RGBA VAE, so nothing here may be defaulted or guessed.
/// </remarks>
public static class QwenImage21ModelSettings
{
    /// <summary>The reference strategy a 2.1 generation model must declare and qualify.</summary>
    public const string ReferenceStrategy = "NativeMultiReference";

    /// <summary>
    /// Reads ONLY the reference pixel budget from a model's NativeMultiReference qualification. Used by
    /// the 2.1 EDITOR row, whose artifacts live in the editor columns while its budget lives with the
    /// capability it was qualified for.
    /// </summary>
    public static int ResolveReferenceResolutionBudget(RegisteredModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var qualification = FindQualification(model);
        if (qualification.Resolution is not { } resolution || resolution <= 0)
        {
            throw new ModelResolutionException(
                $"Qwen-Image-2.1 'Resolution' (the reference pixel budget for TextEncodeQwenImage21) must be positive, "
                + $"but model '{model.DisplayName}' has {qualification.Resolution?.ToString() ?? "none"} in its "
                + $"'{ReferenceStrategy}' qualification. Set 'Resolution' in Model Manager (/model-manager).");
        }

        return resolution;
    }

    public static QwenImage21Refs Resolve(RegisteredModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var qualification = FindQualification(model);

        if (qualification.Resolution is not { } resolution || resolution <= 0)
        {
            throw new ModelResolutionException(
                $"Qwen-Image-2.1 'Resolution' (the reference pixel budget for TextEncodeQwenImage21) must be positive, "
                + $"but model '{model.DisplayName}' has {qualification.Resolution?.ToString() ?? "none"} in its "
                + $"'{ReferenceStrategy}' qualification. Set 'Resolution' in Model Manager (/model-manager).");
        }

        if (qualification.MaxReferences is not { } maxReferences || maxReferences < 1)
        {
            throw new ModelResolutionException(
                $"Qwen-Image-2.1 'MaxReferences' must be at least 1, but model '{model.DisplayName}' has "
                + $"{qualification.MaxReferences?.ToString() ?? "none"} in its '{ReferenceStrategy}' qualification. "
                + "Set 'MaxReferences' in Model Manager (/model-manager).");
        }

        // The generation envelope is REQUIRED configuration, not a code default: a cfg that suits an SDXL
        // checkpoint (5) drives this cfg-1-distilled model out of distribution, which is exactly how the
        // 2026-09-24 blown-out render happened. Sampler/scheduler/steps are validated here so the graph can
        // never fall back to a value nobody qualified.
        if (qualification.Steps is not { } steps || steps < 1)
        {
            throw new ModelResolutionException(
                $"Qwen-Image-2.1 'Steps' must be at least 1 (published examples use 25-50), but model "
                + $"'{model.DisplayName}' has {qualification.Steps?.ToString() ?? "none"} in its '{ReferenceStrategy}' "
                + "qualification. Set 'Steps' in Model Manager (/model-manager).");
        }

        if (qualification.Cfg is not { } cfg || !double.IsFinite(cfg) || cfg <= 0)
        {
            throw new ModelResolutionException(
                $"Qwen-Image-2.1 'Cfg' must be a positive number (the official value is 1.0, where the negative "
                + $"prompt is inert), but model '{model.DisplayName}' has "
                + $"{qualification.Cfg?.ToString() ?? "none"} in its '{ReferenceStrategy}' qualification. Set 'Cfg' in "
                + "Model Manager (/model-manager). Do NOT copy an SDXL cfg such as 5 here.");
        }

        return new QwenImage21Refs(
            UnetName: Require(qualification.UnetName, "UnetName", model),
            TextEncoderName: Require(qualification.TextEncoderName, "TextEncoderName", model),
            VaeName: Require(qualification.VaeName, "VaeName", model),
            ResolutionBudget: resolution,
            MaxReferences: maxReferences,
            Steps: steps,
            Cfg: cfg,
            SamplerName: Require(qualification.SamplerName, "SamplerName", model),
            Scheduler: Require(qualification.Scheduler, "Scheduler", model));
    }

    private static string Require(string? value, string field, RegisteredModel model) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ModelResolutionException(
                $"Qwen-Image-2.1 qualification is missing '{field}' for model '{model.DisplayName}'. "
                + "Add it to the model's CapabilityQualificationsJson in Model Manager (/model-manager).")
            : value.Trim();

    /// <summary>The model's passing NativeMultiReference qualification, or a fail-fast diagnostic.</summary>
    private static Qualification FindQualification(RegisteredModel model)
    {
        var qualifications = ParseQualifications(model.CapabilityQualificationsJson);
        var qualification = qualifications.FirstOrDefault(entry =>
            string.Equals(entry.Strategy, ReferenceStrategy, StringComparison.OrdinalIgnoreCase)
            && entry.Qualified
            && !string.IsNullOrWhiteSpace(entry.ProofId));

        return qualification
            ?? throw new ModelResolutionException(
                $"Qwen-Image-2.1 model '{model.DisplayName}' has no passing '{ReferenceStrategy}' qualification. "
                + $"Add a qualified '{ReferenceStrategy}' entry to CapabilityQualificationsJson in Model Manager "
                + "(/model-manager): the 2.1 graph loads a diffusion model, a text encoder and a VAE, and cannot run "
                + "without each one.");
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
    /// The subset of a capability qualification entry 2.1 needs. Unknown members of an entry are
    /// ignored, which lets one array carry different fields per strategy (as the FLUX pose entry does).
    /// </summary>
    private sealed class Qualification
    {
        [JsonPropertyName("Strategy")] public string Strategy { get; set; } = string.Empty;
        [JsonPropertyName("Qualified")] public bool Qualified { get; set; }
        [JsonPropertyName("ProofId")] public string? ProofId { get; set; }
        [JsonPropertyName("UnetName")] public string? UnetName { get; set; }
        [JsonPropertyName("TextEncoderName")] public string? TextEncoderName { get; set; }
        [JsonPropertyName("VaeName")] public string? VaeName { get; set; }
        [JsonPropertyName("Resolution")] public int? Resolution { get; set; }
        [JsonPropertyName("MaxReferences")] public int? MaxReferences { get; set; }
        [JsonPropertyName("Steps")] public int? Steps { get; set; }
        [JsonPropertyName("Cfg")] public double? Cfg { get; set; }
        [JsonPropertyName("SamplerName")] public string? SamplerName { get; set; }
        [JsonPropertyName("Scheduler")] public string? Scheduler { get; set; }
    }
}
