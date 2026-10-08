using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.ModelManager;

namespace DreamGenClone.Web.Application.ModelManager;

/// <summary>
/// Reads the MiniMax H3 (Ref2VA) artifacts and fixed envelope out of a video model's
/// <c>CapabilityQualificationsJson</c>.
/// </summary>
/// <remarks>
/// Strict and no-fallback, mirroring <see cref="Krea2ModelSettings"/> and <see cref="QwenImage21ModelSettings"/>:
/// every value is a configured field, and a missing one fails fast naming the exact field. Nothing here may be
/// defaulted or guessed - H3 is a split model whose four artifact names are not derivable from the model
/// identifier (a guessed one is a 400 from ComfyUI), its frame envelope decides whether the clip is inside the
/// trained band, and its loudness target is what the delivered audio is normalized to.
/// </remarks>
public static class MiniMaxH3ModelSettings
{
    /// <summary>
    /// The qualification a MiniMax H3 video model must declare and qualify. Deliberately not a reference strategy:
    /// it carries the artifacts, the sampler envelope, the frame policy, the reference caps and the loudness target
    /// the render needs, and nothing about identity (H3 carries identity through its ordered reference images).
    /// </summary>
    public const string Ref2VAStrategy = "MiniMaxH3Ref2VA";

    public static MiniMaxH3Refs Resolve(RegisteredModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var qualification = FindQualification(model);

        var steps = RequirePositive(qualification.Steps, "Steps", model);
        var denoise = RequireUnitRange(qualification.Denoise, "Denoise", model);
        var fps = RequirePositive(qualification.Fps, "Fps", model);
        var bitDepth = RequirePositive(qualification.BitDepth, "BitDepth", model);

        var framePolicy = ResolveFramePolicy(qualification, model);

        var maxReferenceImages = RequirePositive(qualification.MaxReferenceImages, "MaxReferenceImages", model);
        var maxReferenceVideos = RequireNonNegative(qualification.MaxReferenceVideos, "MaxReferenceVideos", model);
        var maxReferenceAudios = RequireNonNegative(qualification.MaxReferenceAudios, "MaxReferenceAudios", model);

        var loudness = qualification.LoudnessTargetLufs;
        if (loudness is not { } target || !double.IsFinite(target) || target is < -60 or > 0)
        {
            throw Missing(
                $"LoudnessTargetLufs (a value between -60 and 0, for example -16) has "
                + $"{Format(loudness)}",
                model);
        }

        var defaultWidth = RequirePositive(qualification.DefaultWidth, "DefaultWidth", model);
        var defaultHeight = RequirePositive(qualification.DefaultHeight, "DefaultHeight", model);
        if (defaultWidth % 32 != 0 || defaultHeight % 32 != 0)
        {
            throw Missing(
                $"DefaultWidth/DefaultHeight (both must be multiples of the node's step, 32; {defaultWidth}x{defaultHeight}",
                model);
        }

        var defaultRefImageSize = Require(qualification.DefaultRefImageSize, "DefaultRefImageSize", model);
        if (defaultRefImageSize is not ("match" or "max"))
        {
            throw Missing(
                $"DefaultRefImageSize (must be 'match' or 'max'; '{defaultRefImageSize}'",
                model);
        }

        var device = Require(qualification.TextEncoderDevice, "TextEncoderDevice", model);

        var renderTimeout = RequirePositive(qualification.RenderTimeoutSeconds, "RenderTimeoutSeconds", model);
        if (renderTimeout < 60)
        {
            throw Missing(
                $"RenderTimeoutSeconds ({renderTimeout} is under a minute, but a trained-range H3 render takes "
                + "~25 to ~100 minutes",
                model);
        }

        // B-156 C-13: continuations accumulate drift (~+4 % contrast and ~1/3 treble per join, measured), so the
        // chain length is a configured policy, not a code default. Missing => fail fast naming the setting.
        var maxContinuationChainLength =
            RequirePositive(qualification.MaxContinuationChainLength, "MaxContinuationChainLength", model);
        if (maxContinuationChainLength < 1)
        {
            throw Missing(
                $"MaxContinuationChainLength ({maxContinuationChainLength} would allow no continuation at all, "
                + "which is not a policy, it is a disabled feature",
                model);
        }

        return new MiniMaxH3Refs(
            DitName: Require(qualification.DitName, "DitName", model),
            TextEncoderName: Require(qualification.TextEncoderName, "TextEncoderName", model),
            TextEncoderDevice: device,
            VideoVaeName: Require(qualification.VideoVae, "VideoVae", model),
            AudioVaeName: Require(qualification.VideoAudioVae, "VideoAudioVae", model),
            SamplerName: Require(qualification.SamplerName, "SamplerName", model),
            Scheduler: Require(qualification.Scheduler, "Scheduler", model),
            Steps: steps,
            Denoise: denoise,
            Fps: fps,
            BitDepth: bitDepth,
            DefaultWidth: defaultWidth,
            DefaultHeight: defaultHeight,
            DefaultRefImageSize: defaultRefImageSize,
            FramePolicy: framePolicy,
            MaxReferenceImages: maxReferenceImages,
            MaxReferenceVideos: maxReferenceVideos,
            MaxReferenceAudios: maxReferenceAudios,
            LoudnessTargetLufs: loudness!.Value,
            RenderTimeoutSeconds: renderTimeout,
            FfmpegPath: Require(qualification.FfmpegPath, "FfmpegPath", model),
            MaxContinuationChainLength: maxContinuationChainLength);
    }

    private static MiniMaxH3FramePolicy ResolveFramePolicy(Qualification qualification, RegisteredModel model)
    {
        var minFrames = RequirePositive(qualification.FrameMinFrames, "FrameMinFrames", model);
        var maxFrames = RequirePositive(qualification.FrameMaxFrames, "FrameMaxFrames", model);
        var frameStep = RequirePositive(qualification.FrameStep, "FrameStep", model);
        var trainedMin = RequirePositive(qualification.TrainedMinFrames, "TrainedMinFrames", model);
        var trainedMax = RequirePositive(qualification.TrainedMaxFrames, "TrainedMaxFrames", model);
        var warnAbove = RequirePositive(qualification.WarnAboveFrames, "WarnAboveFrames", model);
        var presetShort = RequirePositive(qualification.PresetShortFrames, "PresetShortFrames", model);
        var presetLong = RequirePositive(qualification.PresetLongFrames, "PresetLongFrames", model);

        var policy = new MiniMaxH3FramePolicy(
            MinFrames: minFrames,
            MaxFrames: maxFrames,
            FrameStep: frameStep,
            TrainedMinFrames: trainedMin,
            TrainedMaxFrames: trainedMax,
            WarnAboveFrames: warnAbove,
            PresetShortFrames: presetShort,
            PresetLongFrames: presetLong);

        if (trainedMin < minFrames || trainedMax > maxFrames || trainedMax < trainedMin)
        {
            throw Missing(
                $"TrainedMinFrames/TrainedMaxFrames (the trained band must sit inside the node's "
                + $"{minFrames}-{maxFrames} range; configured {trainedMin}-{trainedMax}",
                model);
        }

        foreach (var (name, value) in new[]
                 {
                     ("TrainedMinFrames", trainedMin),
                     ("TrainedMaxFrames", trainedMax),
                     ("PresetShortFrames", presetShort),
                     ("PresetLongFrames", presetLong)
                 })
        {
            if (!policy.IsAccepted(value))
            {
                throw Missing(
                    $"{name} ({value} is not a length this node accepts: it must be between {minFrames} and "
                    + $"{maxFrames} congruent to {minFrames} modulo {frameStep}",
                    model);
            }
        }

        if (warnAbove > trainedMax || warnAbove < trainedMin)
        {
            throw Missing(
                $"WarnAboveFrames (the warning threshold must sit inside the trained band "
                + $"{trainedMin}-{trainedMax}; configured {warnAbove}",
                model);
        }

        return policy;
    }

    private static string Require(string? value, string field, RegisteredModel model) =>
        string.IsNullOrWhiteSpace(value)
            ? throw Missing($"{field} is not set", model)
            : value.Trim();

    private static int RequirePositive(int? value, string field, RegisteredModel model) =>
        value is not { } number || number <= 0
            ? throw Missing($"{field} ({Format(value)} is not a positive whole number", model)
            : number;

    private static int RequireNonNegative(int? value, string field, RegisteredModel model) =>
        value is not { } number || number < 0
            ? throw Missing($"{field} ({Format(value)} is not a whole number of zero or more", model)
            : number;

    private static double RequireUnitRange(double? value, string field, RegisteredModel model) =>
        value is not { } number || !double.IsFinite(number) || number is <= 0 or > 1
            ? throw Missing($"{field} ({Format(value)} is not greater than 0 and at most 1", model)
            : number;

    private static string Format(int? value) => value?.ToString() ?? "no value";

    private static string Format(double? value) => value?.ToString() ?? "no value";

    private static ModelResolutionException Missing(string detail, RegisteredModel model) =>
        new(
            $"MiniMax H3 video model '{model.DisplayName}' is incompletely configured: {detail}). "
            + $"Add every field of the '{Ref2VAStrategy}' qualification to the model in Model Manager "
            + "(/model-manager), or seed it with the DbQuery 'h3-video-configure' command. No artifact name, "
            + "sampler value, frame bound or loudness target is ever guessed.");

    /// <summary>The model's passing Ref2VA qualification, or a fail-fast diagnostic.</summary>
    private static Qualification FindQualification(RegisteredModel model)
    {
        var qualifications = ParseQualifications(model.CapabilityQualificationsJson);
        var qualification = qualifications.FirstOrDefault(entry =>
            string.Equals(entry.Strategy, Ref2VAStrategy, StringComparison.OrdinalIgnoreCase)
            && entry.Qualified
            && !string.IsNullOrWhiteSpace(entry.ProofId));

        return qualification
            ?? throw new ModelResolutionException(
                $"Video model '{model.DisplayName}' has no passing '{Ref2VAStrategy}' qualification. Add a "
                + $"qualified '{Ref2VAStrategy}' entry to CapabilityQualificationsJson in Model Manager "
                + "(/model-manager): the H3 graph loads a diffusion transformer, a text encoder and separate video "
                + "and audio VAEs, and renders inside a fixed frame envelope and loudness target, and cannot run "
                + "without each one configured.");
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
    /// The subset of a capability qualification entry H3 needs. Unknown members of an entry are ignored, which
    /// lets one array carry different fields per strategy (as the Krea 2 TextToImage and Qwen
    /// NativeMultiReference entries do).
    /// </summary>
    private sealed class Qualification
    {
        [JsonPropertyName("Strategy")] public string Strategy { get; set; } = string.Empty;
        [JsonPropertyName("Qualified")] public bool Qualified { get; set; }
        [JsonPropertyName("ProofId")] public string? ProofId { get; set; }
        [JsonPropertyName("DitName")] public string? DitName { get; set; }
        [JsonPropertyName("TextEncoderName")] public string? TextEncoderName { get; set; }
        [JsonPropertyName("TextEncoderDevice")] public string? TextEncoderDevice { get; set; }
        [JsonPropertyName("VideoVae")] public string? VideoVae { get; set; }
        [JsonPropertyName("VideoAudioVae")] public string? VideoAudioVae { get; set; }
        [JsonPropertyName("SamplerName")] public string? SamplerName { get; set; }
        [JsonPropertyName("Scheduler")] public string? Scheduler { get; set; }
        [JsonPropertyName("Steps")] public int? Steps { get; set; }
        [JsonPropertyName("Denoise")] public double? Denoise { get; set; }
        [JsonPropertyName("Fps")] public int? Fps { get; set; }
        [JsonPropertyName("BitDepth")] public int? BitDepth { get; set; }
        [JsonPropertyName("DefaultWidth")] public int? DefaultWidth { get; set; }
        [JsonPropertyName("DefaultHeight")] public int? DefaultHeight { get; set; }
        [JsonPropertyName("DefaultRefImageSize")] public string? DefaultRefImageSize { get; set; }
        [JsonPropertyName("FrameMinFrames")] public int? FrameMinFrames { get; set; }
        [JsonPropertyName("FrameMaxFrames")] public int? FrameMaxFrames { get; set; }
        [JsonPropertyName("FrameStep")] public int? FrameStep { get; set; }
        [JsonPropertyName("TrainedMinFrames")] public int? TrainedMinFrames { get; set; }
        [JsonPropertyName("TrainedMaxFrames")] public int? TrainedMaxFrames { get; set; }
        [JsonPropertyName("WarnAboveFrames")] public int? WarnAboveFrames { get; set; }
        [JsonPropertyName("PresetShortFrames")] public int? PresetShortFrames { get; set; }
        [JsonPropertyName("PresetLongFrames")] public int? PresetLongFrames { get; set; }
        [JsonPropertyName("MaxReferenceImages")] public int? MaxReferenceImages { get; set; }
        [JsonPropertyName("MaxReferenceVideos")] public int? MaxReferenceVideos { get; set; }
        [JsonPropertyName("MaxReferenceAudios")] public int? MaxReferenceAudios { get; set; }
        [JsonPropertyName("LoudnessTargetLufs")] public double? LoudnessTargetLufs { get; set; }
        [JsonPropertyName("RenderTimeoutSeconds")] public int? RenderTimeoutSeconds { get; set; }
        [JsonPropertyName("FfmpegPath")] public string? FfmpegPath { get; set; }
        [JsonPropertyName("MaxContinuationChainLength")] public int? MaxContinuationChainLength { get; set; }
    }
}
