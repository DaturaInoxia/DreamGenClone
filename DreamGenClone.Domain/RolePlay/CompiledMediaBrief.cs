using System.Text.Json;

namespace DreamGenClone.Domain.RolePlay;

public enum MediaProductionKind
{
    StillImage = 1,
    Speech = 2,
    AmbienceEffects = 3,
    Music = 4,
    Video = 5,
    VideoWithAudio = 6,
    LipSyncPerformance = 7
}

public enum MediaCompilerStatus
{
    Complete = 1,
    Failed = 2
}

public enum RequiredIntentCoverageStatus
{
    Supported = 1,
    Unsupported = 2
}

public enum MediaCompilerCapability
{
    FrozenVisualState = 1,
    TypedMediaReferences = 2,
    SpeechText = 3,
    SpeechPerformance = 4,
    RealizedSpeechAlignment = 5,
    Ambience = 6,
    SoundEffects = 7,
    MusicSections = 8,
    VideoKeyStates = 9,
    VideoActionArc = 10,
    VideoCameraMotion = 11,
    ExternalAudioReferences = 12,
    NativeVideoAudio = 13,
    LipSyncWindows = 14
}

public sealed record MediaCompilerTargetProfile(
    string ProfileId,
    string ProfileVersion,
    MediaProductionKind MediaKind,
    string FamilyKey,
    string CompilerKey,
    string CompilerVersion,
    IReadOnlySet<MediaCompilerCapability> Capabilities,
    string ProviderRequestContractVersion);

public sealed record CompiledMediaLineage(
    string CatalogueId,
    string BeatId,
    string BeatProductionPlanId,
    int BeatProductionPlanVersion,
    string MomentSetId,
    int MomentSetVersion,
    string MomentId,
    string MomentEnrichmentId,
    int MomentEnrichmentRevision);

public sealed record RequiredIntentCoverageEntry(
    string IntentName,
    RequiredIntentCoverageStatus Status,
    string Reason);

public sealed record RequiredIntentCoverageReport(
    IReadOnlyList<RequiredIntentCoverageEntry> Entries)
{
    public bool HasUnsupportedRequiredIntent =>
        Entries.Any(entry => entry.Status == RequiredIntentCoverageStatus.Unsupported);
}

public sealed record MediaAlignmentInterval(
    string Value,
    decimal StartSeconds,
    decimal EndSeconds);

public sealed record RealizedMediaAlignment(
    decimal ActualDurationSeconds,
    int? SampleRateHz,
    decimal? FramesPerSecond,
    IReadOnlyList<MediaAlignmentInterval> CharacterIntervals,
    IReadOnlyList<MediaAlignmentInterval> WordIntervals,
    string ProviderRequestId,
    IReadOnlyList<string> SourceCueIds,
    DateTime CreatedUtc);

public sealed record ApprovedMediaDerivative(
    string Id,
    int Version,
    MediaProductionKind MediaKind,
    string SourceBriefId,
    string SourceBriefProfileVersion,
    IReadOnlyList<string> SourceCueIds,
    string AssetId,
    string AssetChecksum,
    RealizedMediaAlignment? RealizedAlignment,
    DateTime ApprovedUtc,
    DateTime CreatedUtc);

public sealed record CompiledMediaBrief(
    string Id,
    MediaProductionKind MediaKind,
    string TargetProfileId,
    string TargetProfileVersion,
    string FamilyKey,
    string CompilerKey,
    string CompilerVersion,
    string ProviderRequestContractVersion,
    CompiledMediaLineage Lineage,
    IReadOnlyList<string> CanonicalSourceIds,
    string SemanticInputSnapshotJson,
    string ProviderRequestSnapshotJson,
    string RequiredIntentCoverageJson,
    MediaCompilerStatus Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTime CreatedUtc,
    DateTime CompletedUtc);

public static class CompiledMediaContractValidator
{
    public static void ValidateTargetProfile(MediaCompilerTargetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Require(profile.ProfileId, nameof(profile.ProfileId));
        Require(profile.ProfileVersion, nameof(profile.ProfileVersion));
        Require(profile.FamilyKey, nameof(profile.FamilyKey));
        Require(profile.CompilerKey, nameof(profile.CompilerKey));
        Require(profile.CompilerVersion, nameof(profile.CompilerVersion));
        Require(profile.ProviderRequestContractVersion, nameof(profile.ProviderRequestContractVersion));
        if (!Enum.IsDefined(profile.MediaKind))
            throw new InvalidOperationException("Target profile media kind is invalid.");
        if (profile.Capabilities is null || profile.Capabilities.Count == 0)
            throw new InvalidOperationException("Target profile capabilities are required.");
        if (profile.Capabilities.Any(capability => !Enum.IsDefined(capability)))
            throw new InvalidOperationException("Target profile contains an invalid capability.");
    }

    public static void ValidateBrief(CompiledMediaBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        Require(brief.Id, nameof(brief.Id));
        Require(brief.TargetProfileId, nameof(brief.TargetProfileId));
        Require(brief.TargetProfileVersion, nameof(brief.TargetProfileVersion));
        Require(brief.FamilyKey, nameof(brief.FamilyKey));
        Require(brief.CompilerKey, nameof(brief.CompilerKey));
        Require(brief.CompilerVersion, nameof(brief.CompilerVersion));
        Require(brief.ProviderRequestContractVersion, nameof(brief.ProviderRequestContractVersion));
        ValidateLineage(brief.Lineage);
        if (brief.CanonicalSourceIds is null || brief.CanonicalSourceIds.Count == 0 ||
            brief.CanonicalSourceIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Canonical source ids are required.");
        ValidateJson(brief.SemanticInputSnapshotJson, nameof(brief.SemanticInputSnapshotJson));
        ValidateJson(brief.ProviderRequestSnapshotJson, nameof(brief.ProviderRequestSnapshotJson));
        ValidateJson(brief.RequiredIntentCoverageJson, nameof(brief.RequiredIntentCoverageJson));
        RequireUtc(brief.CreatedUtc, nameof(brief.CreatedUtc));
        RequireUtc(brief.CompletedUtc, nameof(brief.CompletedUtc));
        if (brief.CompletedUtc < brief.CreatedUtc)
            throw new InvalidOperationException("Brief completion timestamp cannot precede creation.");
        if (brief.Status == MediaCompilerStatus.Complete &&
            (!string.IsNullOrWhiteSpace(brief.ErrorCode) || !string.IsNullOrWhiteSpace(brief.ErrorMessage)))
            throw new InvalidOperationException("A complete brief cannot contain an error.");
        if (brief.Status == MediaCompilerStatus.Failed)
        {
            Require(brief.ErrorCode, nameof(brief.ErrorCode));
            Require(brief.ErrorMessage, nameof(brief.ErrorMessage));
        }
    }

    public static void ValidateDerivative(ApprovedMediaDerivative derivative)
    {
        ArgumentNullException.ThrowIfNull(derivative);
        Require(derivative.Id, nameof(derivative.Id));
        Require(derivative.SourceBriefId, nameof(derivative.SourceBriefId));
        Require(derivative.SourceBriefProfileVersion, nameof(derivative.SourceBriefProfileVersion));
        Require(derivative.AssetId, nameof(derivative.AssetId));
        Require(derivative.AssetChecksum, nameof(derivative.AssetChecksum));
        if (derivative.Version <= 0)
            throw new InvalidOperationException("Derivative version must be positive.");
        if (derivative.SourceCueIds is null || derivative.SourceCueIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Derivative source cue ids are invalid.");
        RequireUtc(derivative.ApprovedUtc, nameof(derivative.ApprovedUtc));
        RequireUtc(derivative.CreatedUtc, nameof(derivative.CreatedUtc));
        if (derivative.RealizedAlignment is not null)
            ValidateAlignment(derivative.RealizedAlignment);
    }

    public static void ValidateAlignment(RealizedMediaAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(alignment);
        if (alignment.ActualDurationSeconds <= 0)
            throw new InvalidOperationException("Realized duration must be positive.");
        if (alignment.SampleRateHz is <= 0)
            throw new InvalidOperationException("Sample rate must be positive when supplied.");
        if (alignment.FramesPerSecond is <= 0)
            throw new InvalidOperationException("Frames per second must be positive when supplied.");
        Require(alignment.ProviderRequestId, nameof(alignment.ProviderRequestId));
        if (alignment.SourceCueIds is null || alignment.SourceCueIds.Count == 0 ||
            alignment.SourceCueIds.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("Realized alignment source cue ids are required.");
        ValidateIntervals(alignment.CharacterIntervals, alignment.ActualDurationSeconds, "character");
        ValidateIntervals(alignment.WordIntervals, alignment.ActualDurationSeconds, "word");
        RequireUtc(alignment.CreatedUtc, nameof(alignment.CreatedUtc));
    }

    public static void ValidateLineage(CompiledMediaLineage lineage)
    {
        ArgumentNullException.ThrowIfNull(lineage);
        Require(lineage.CatalogueId, nameof(lineage.CatalogueId));
        Require(lineage.BeatId, nameof(lineage.BeatId));
        Require(lineage.BeatProductionPlanId, nameof(lineage.BeatProductionPlanId));
        Require(lineage.MomentSetId, nameof(lineage.MomentSetId));
        Require(lineage.MomentId, nameof(lineage.MomentId));
        Require(lineage.MomentEnrichmentId, nameof(lineage.MomentEnrichmentId));
        if (lineage.BeatProductionPlanVersion <= 0 || lineage.MomentSetVersion <= 0 || lineage.MomentEnrichmentRevision <= 0)
            throw new InvalidOperationException("All lineage versions and revisions must be positive.");
    }

    public static void ValidateJson(string json, string label)
    {
        Require(json, label);
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} must contain valid JSON.", exception);
        }
    }

    private static void ValidateIntervals(
        IReadOnlyList<MediaAlignmentInterval> intervals,
        decimal duration,
        string label)
    {
        if (intervals is null)
            throw new InvalidOperationException($"Realized {label} intervals are required, even when empty.");
        foreach (var interval in intervals)
        {
            Require(interval.Value, $"Realized {label} interval value");
            if (interval.StartSeconds < 0 || interval.EndSeconds <= interval.StartSeconds || interval.EndSeconds > duration)
                throw new InvalidOperationException($"Realized {label} interval is outside the derivative duration.");
        }
    }

    private static void RequireUtc(DateTime value, string label)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException($"{label} must be an explicit UTC timestamp.");
    }

    private static void Require(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{label} is required.");
    }
}