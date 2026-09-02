using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class DeterministicMultimodalMediaCompiler : IMultimodalMediaCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public DeterministicMultimodalMediaCompiler(MediaCompilerDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(descriptor.FamilyKey) || string.IsNullOrWhiteSpace(descriptor.CompilerKey) ||
            string.IsNullOrWhiteSpace(descriptor.CompilerVersion) || descriptor.Capabilities is null)
            throw new InvalidOperationException("A complete deterministic compiler descriptor is required.");
        Descriptor = descriptor;
    }

    public MediaCompilerDescriptor Descriptor { get; }

    public CompiledMediaBrief Compile(CompileMediaBriefRequest request, DateTime createdUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (createdUtc.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("Compilation timestamp must be UTC.");
        ValidateLineage(request);
        ValidateProfile(request.TargetProfile);

        var projection = BuildProjection(request);
        var coverage = BuildCoverage(projection.RequiredCapabilities);
        var coverageJson = JsonSerializer.Serialize(new RequiredIntentCoverageReport(coverage), JsonOptions);
        var semanticJson = JsonSerializer.Serialize(projection.SemanticInput, JsonOptions);
        var providerRequestJson = JsonSerializer.Serialize(new
        {
            contractVersion = request.TargetProfile.ProviderRequestContractVersion,
            mediaKind = request.TargetProfile.MediaKind,
            semanticInput = projection.SemanticInput
        }, JsonOptions);
        var unsupported = coverage.Where(entry => entry.Status == RequiredIntentCoverageStatus.Unsupported).ToList();
        var status = unsupported.Count == 0 ? MediaCompilerStatus.Complete : MediaCompilerStatus.Failed;
        var errorMessage = unsupported.Count == 0
            ? null
            : string.Join("; ", unsupported.Select(entry => $"{entry.IntentName}: {entry.Reason}"));
        var lineage = CreateLineage(request);
        var brief = new CompiledMediaBrief(
            Guid.NewGuid().ToString(), request.TargetProfile.MediaKind,
            request.TargetProfile.ProfileId, request.TargetProfile.ProfileVersion,
            request.TargetProfile.FamilyKey, request.TargetProfile.CompilerKey,
            request.TargetProfile.CompilerVersion, request.TargetProfile.ProviderRequestContractVersion,
            lineage,
            [request.BeatProductionPlan.Id, request.MomentSet.Id, request.Moment.MomentId, request.MomentEnrichment.Id],
            semanticJson, providerRequestJson, coverageJson, status,
            status == MediaCompilerStatus.Failed ? "UnsupportedRequiredIntent" : null,
            errorMessage, createdUtc, createdUtc);
        CompiledMediaContractValidator.ValidateBrief(brief);
        return brief;
    }

    private Projection BuildProjection(CompileMediaBriefRequest request) => request.TargetProfile.MediaKind switch
    {
        MediaProductionKind.StillImage => BuildStill(request),
        MediaProductionKind.Speech => BuildSpeech(request),
        MediaProductionKind.AmbienceEffects => BuildAmbienceEffects(request),
        MediaProductionKind.Music => BuildMusic(request),
        MediaProductionKind.Video => BuildVideo(request, includeNativeAudio: false),
        MediaProductionKind.VideoWithAudio => BuildVideo(request, includeNativeAudio: true),
        MediaProductionKind.LipSyncPerformance => BuildLipSync(request),
        _ => throw new InvalidOperationException($"Unsupported media kind '{request.TargetProfile.MediaKind}'.")
    };

    private static Projection BuildStill(CompileMediaBriefRequest request)
    {
        RequireNoCoverage(request, MediaProductionKind.StillImage);
        return new Projection(new
        {
            lineage = CreateLineage(request),
            moment = new
            {
                request.Moment.MomentId,
                request.Moment.TemporalAnchor,
                request.Moment.FrozenState,
                request.Moment.VisibleAction,
                participantSummary = ParseJson(request.Moment.ParticipantSummaryJson, "Moment participant summary"),
                request.Moment.CompositionRationale,
                productionRoles = ParseJson(request.Moment.ProductionRolesJson, "Moment production roles")
            },
            frozenState = ParseJson(request.MomentEnrichment.FrozenStateContractJson, "Frozen state contract"),
            continuity = new
            {
                start = ParseJson(request.BeatProductionPlan.StartContinuityJson, "Start continuity"),
                end = ParseJson(request.BeatProductionPlan.EndContinuityJson, "End continuity")
            },
            typedReferences = ParseJson(request.BeatProductionPlan.TypedReferencesJson, "Typed references"),
            videoKeyState = ParseJson(request.MomentEnrichment.VideoKeyStateJson, "Video key state")
        }, new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.FrozenVisualState,
            MediaCompilerCapability.TypedMediaReferences
        });
    }

    private static Projection BuildSpeech(CompileMediaBriefRequest request)
    {
        RequireNoCoverage(request, MediaProductionKind.Speech);
        var cues = SelectDialogue(request.BeatProductionPlan, request.DialogueCueIds, requireAny: true);
        if (request.SoundCueIds.Count != 0 || request.MusicSectionKeys.Count != 0)
            throw new InvalidOperationException("Speech compilation accepts only dialogue or narration cue ids.");
        return new Projection(new
        {
            lineage = CreateLineage(request),
            cues = cues.Select(cue => new
            {
                cue.Id,
                cue.Kind,
                cue.EventKey,
                cue.ExactSourceText,
                cue.DisplayText,
                cue.NormalizedSpokenText,
                normalization = new { cue.NormalizationMethod, cue.NormalizationVersion },
                cue.SourceInteractionId,
                cue.StartOffset,
                cue.EndOffset,
                cue.SpeakerCharacterId,
                cue.AddresseeCharacterIds,
                cue.PerformanceIntent,
                cue.Window,
                cue.LipSyncRelevant
            }).ToList(),
            typedReferences = ParseJson(request.BeatProductionPlan.TypedReferencesJson, "Typed references")
        }, new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.SpeechText,
            MediaCompilerCapability.SpeechPerformance
        });
    }

    private static Projection BuildAmbienceEffects(CompileMediaBriefRequest request)
    {
        RequireNoCoverage(request, MediaProductionKind.AmbienceEffects);
        if (request.DialogueCueIds.Count != 0 || request.MusicSectionKeys.Count != 0)
            throw new InvalidOperationException("Ambience/effects compilation accepts only sound cue ids.");
        var cues = SelectSound(request.BeatProductionPlan, request.SoundCueIds, requireAny: true);
        var capabilities = new HashSet<MediaCompilerCapability>();
        if (cues.Any(cue => cue.Kind == SceneBeatSoundKind.Ambience)) capabilities.Add(MediaCompilerCapability.Ambience);
        if (cues.Any(cue => cue.Kind == SceneBeatSoundKind.SoundEffect)) capabilities.Add(MediaCompilerCapability.SoundEffects);
        return new Projection(new
        {
            lineage = CreateLineage(request),
            ambiencePlan = ParseJson(request.BeatProductionPlan.AmbiencePlanJson, "Ambience plan"),
            cues
        }, capabilities);
    }

    private static Projection BuildMusic(CompileMediaBriefRequest request)
    {
        RequireNoCoverage(request, MediaProductionKind.Music);
        if (request.DialogueCueIds.Count != 0 || request.SoundCueIds.Count != 0)
            throw new InvalidOperationException("Music compilation accepts only music section keys.");
        var sections = SelectMusicSections(request.BeatProductionPlan.MusicPlanJson, request.MusicSectionKeys);
        return new Projection(new
        {
            lineage = CreateLineage(request),
            sections,
            conditioningReferences = ParseJson(request.BeatProductionPlan.TypedReferencesJson, "Typed references")
        }, new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.MusicSections,
            MediaCompilerCapability.TypedMediaReferences
        });
    }

    private static Projection BuildVideo(CompileMediaBriefRequest request, bool includeNativeAudio)
    {
        var coverage = SelectCoverage(request);
        ValidateSelectionMatchesCoverage(request, coverage);
        var ownership = SceneVideoAudioOwnershipValidator.Validate(coverage);
        var dialogue = SelectDialogue(request.BeatProductionPlan, coverage.DialogueCueIds, requireAny: false);
        var sounds = SelectSound(request.BeatProductionPlan, coverage.SoundCueIds, requireAny: false);
        var music = SelectMusicSections(request.BeatProductionPlan.MusicPlanJson, coverage.MusicSectionKeys);
        var nativeIds = ownership
            .Where(item => item.OwnershipIntent is "GeneratedWithVideo" or "Hybrid")
            .Select(item => item.CueId).ToHashSet(StringComparer.Ordinal);
        var externalIds = ownership
            .Where(item => item.OwnershipIntent is "ExternalMix" or "Hybrid")
            .Select(item => item.CueId).ToHashSet(StringComparer.Ordinal);
        var required = new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.VideoKeyStates,
            MediaCompilerCapability.VideoActionArc,
            MediaCompilerCapability.VideoCameraMotion,
            MediaCompilerCapability.TypedMediaReferences,
            MediaCompilerCapability.ExternalAudioReferences
        };
        if (includeNativeAudio) required.Add(MediaCompilerCapability.NativeVideoAudio);

        return new Projection(new
        {
            lineage = CreateLineage(request),
            coverage = new
            {
                coverage.Id,
                coverage.CoverageKey,
                coverage.CoverageKind,
                coverage.Window,
                coverage.SourceEventKeys,
                coverage.RequiredMomentRoles,
                coverage.PermittedActionPhases,
                coverage.CameraIntent,
                coverage.LensIntent,
                coverage.MotionIntent,
                coverage.PacingIntent,
                coverage.PerformanceIntent,
                coverage.DurationFitPolicy,
                coverage.LipSyncRequired
            },
            selectedMoment = new { request.Moment.MomentId, request.Moment.FrozenState, request.Moment.VisibleAction },
            frozenState = ParseJson(request.MomentEnrichment.FrozenStateContractJson, "Frozen state contract"),
            keyState = ParseJson(request.MomentEnrichment.VideoKeyStateJson, "Video key state"),
            actionArc = ParseJson(request.BeatProductionPlan.ActionArcJson, "Action arc"),
            continuity = new
            {
                start = ParseJson(request.BeatProductionPlan.StartContinuityJson, "Start continuity"),
                end = ParseJson(request.BeatProductionPlan.EndContinuityJson, "End continuity")
            },
            references = coverage.References,
            audio = includeNativeAudio ? new
            {
                ownership,
                dialogue = dialogue.Where(cue => nativeIds.Contains(cue.Id)).ToList(),
                sounds = sounds.Where(cue => nativeIds.Contains(cue.Id)).ToList(),
                music = music.Where(section => nativeIds.Contains(MusicCueId(request.BeatProductionPlan.Id, section))).ToList(),
                externalCueIds = externalIds.Order(StringComparer.Ordinal).ToList()
            } : new
            {
                ownership,
                dialogue = new List<SceneBeatDialogueCue>(),
                sounds = new List<SceneBeatSoundCue>(),
                music = new List<JsonElement>(),
                externalCueIds = externalIds.Order(StringComparer.Ordinal).ToList()
            }
        }, required);
    }

    private static Projection BuildLipSync(CompileMediaBriefRequest request)
    {
        var coverage = SelectCoverage(request);
        ValidateSelectionMatchesCoverage(request, coverage);
        if (!coverage.LipSyncRequired)
            throw new InvalidOperationException($"Video coverage '{coverage.Id}' does not require lip sync.");
        var cues = SelectDialogue(request.BeatProductionPlan, coverage.DialogueCueIds, requireAny: true);
        if (cues.Any(cue => !cue.LipSyncRelevant))
            throw new InvalidOperationException("Lip-sync compilation contains a cue that is not lip-sync relevant.");
        var visual = request.ApprovedVisualDerivative
            ?? throw new InvalidOperationException("Lip-sync compilation requires an approved visual derivative.");
        var speech = request.ApprovedSpeechDerivative
            ?? throw new InvalidOperationException("Lip-sync compilation requires an approved speech derivative.");
        CompiledMediaContractValidator.ValidateDerivative(visual);
        CompiledMediaContractValidator.ValidateDerivative(speech);
        if (speech.RealizedAlignment is null)
            throw new InvalidOperationException("Lip-sync compilation requires realized speech alignment.");
        var cueIds = cues.Select(cue => cue.Id).ToHashSet(StringComparer.Ordinal);
        if (!cueIds.SetEquals(speech.SourceCueIds) || !cueIds.SetEquals(speech.RealizedAlignment.SourceCueIds))
            throw new InvalidOperationException("Approved speech derivative cue ids do not exactly match lip-sync coverage cues.");

        return new Projection(new
        {
            lineage = CreateLineage(request),
            coverage = new
            {
                coverage.Id,
                coverage.Window,
                coverage.DurationFitPolicy,
                coverage.PerformanceIntent,
                coverage.RequiredMomentRoles
            },
            speechCues = cues,
            visualDerivative = new { visual.Id, visual.Version, visual.AssetId, visual.AssetChecksum },
            speechDerivative = new { speech.Id, speech.Version, speech.AssetId, speech.AssetChecksum, speech.RealizedAlignment },
            visualState = ParseJson(request.MomentEnrichment.FrozenStateContractJson, "Frozen state contract"),
            keyState = ParseJson(request.MomentEnrichment.VideoKeyStateJson, "Video key state"),
            sourceReferences = coverage.References
        }, new HashSet<MediaCompilerCapability>
        {
            MediaCompilerCapability.LipSyncWindows,
            MediaCompilerCapability.RealizedSpeechAlignment,
            MediaCompilerCapability.SpeechPerformance,
            MediaCompilerCapability.FrozenVisualState,
            MediaCompilerCapability.TypedMediaReferences
        });
    }

    private IReadOnlyList<RequiredIntentCoverageEntry> BuildCoverage(IReadOnlySet<MediaCompilerCapability> required) =>
        required.OrderBy(capability => capability).Select(capability =>
            Descriptor.Capabilities.Contains(capability)
                ? new RequiredIntentCoverageEntry(capability.ToString(), RequiredIntentCoverageStatus.Supported,
                    $"Compiler '{Descriptor.CompilerKey}' version '{Descriptor.CompilerVersion}' explicitly supports this required intent.")
                : new RequiredIntentCoverageEntry(capability.ToString(), RequiredIntentCoverageStatus.Unsupported,
                    $"Compiler '{Descriptor.CompilerKey}' version '{Descriptor.CompilerVersion}' does not declare capability '{capability}'."))
            .ToList();

    private void ValidateProfile(MediaCompilerTargetProfile profile)
    {
        CompiledMediaContractValidator.ValidateTargetProfile(profile);
        if (profile.MediaKind != Descriptor.MediaKind ||
            !string.Equals(profile.FamilyKey, Descriptor.FamilyKey, StringComparison.Ordinal) ||
            !string.Equals(profile.CompilerKey, Descriptor.CompilerKey, StringComparison.Ordinal) ||
            !string.Equals(profile.CompilerVersion, Descriptor.CompilerVersion, StringComparison.Ordinal) ||
            !profile.Capabilities.SetEquals(Descriptor.Capabilities))
            throw new InvalidOperationException("Compiler invocation does not exactly match the resolved target profile.");
    }

    internal static void ValidateLineage(CompileMediaBriefRequest request)
    {
        var plan = request.BeatProductionPlan;
        var set = request.MomentSet;
        var moment = request.Moment;
        var enrichment = request.MomentEnrichment;
        if (plan.Status != SceneBeatCatalogueStatus.Complete || set.Status != SceneBeatCatalogueStatus.Complete ||
            enrichment.Status != SceneBeatCatalogueStatus.Complete)
            throw new InvalidOperationException("Compilation requires complete Beat Production Plan, Moment Set, and Moment Enrichment records.");
        if (!string.Equals(set.CatalogueId, plan.CatalogueId, StringComparison.Ordinal) ||
            !string.Equals(set.BeatId, plan.BeatId, StringComparison.Ordinal) ||
            !string.Equals(set.BeatProductionPlanId, plan.Id, StringComparison.Ordinal) ||
            set.BeatProductionPlanVersion != plan.Version)
            throw new InvalidOperationException("Moment Set lineage does not exactly match the Beat Production Plan.");
        var matchingMoments = set.Moments.Where(candidate =>
            string.Equals(candidate.MomentSetId, set.Id, StringComparison.Ordinal) &&
            string.Equals(candidate.MomentId, moment.MomentId, StringComparison.Ordinal)).ToList();
        if (matchingMoments.Count != 1 || !ReferenceEquals(matchingMoments[0], moment) && matchingMoments[0] != moment)
            throw new InvalidOperationException("Selected Moment must be the unique canonical Moment in the supplied Moment Set.");
        if (!string.Equals(enrichment.CatalogueId, plan.CatalogueId, StringComparison.Ordinal) ||
            !string.Equals(enrichment.BeatId, plan.BeatId, StringComparison.Ordinal) ||
            !string.Equals(enrichment.BeatProductionPlanId, plan.Id, StringComparison.Ordinal) ||
            enrichment.BeatProductionPlanVersion != plan.Version ||
            !string.Equals(enrichment.MomentSetId, set.Id, StringComparison.Ordinal) ||
            enrichment.MomentSetVersion != set.Version ||
            !string.Equals(enrichment.MomentId, moment.MomentId, StringComparison.Ordinal))
            throw new InvalidOperationException("Moment Enrichment lineage does not exactly match the selected canonical parents.");
        if (plan.Version <= 0 || set.Version <= 0 || enrichment.Revision <= 0)
            throw new InvalidOperationException("Canonical lineage versions and revisions must be positive.");
    }

    internal static CompiledMediaLineage CreateLineage(CompileMediaBriefRequest request) => new(
        request.BeatProductionPlan.CatalogueId,
        request.BeatProductionPlan.BeatId,
        request.BeatProductionPlan.Id,
        request.BeatProductionPlan.Version,
        request.MomentSet.Id,
        request.MomentSet.Version,
        request.Moment.MomentId,
        request.MomentEnrichment.Id,
        request.MomentEnrichment.Revision);

    private static SceneVideoCoveragePlan SelectCoverage(CompileMediaBriefRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VideoCoveragePlanId))
            throw new InvalidOperationException($"{request.TargetProfile.MediaKind} compilation requires an exact video coverage plan id.");
        var matches = request.BeatProductionPlan.VideoCoveragePlans
            .Where(item => string.Equals(item.Id, request.VideoCoveragePlanId, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"Video coverage plan id '{request.VideoCoveragePlanId}' was absent or ambiguous.");
        var coverage = matches[0];
        if (coverage.ReviewStatus != ProductionReviewStatus.Validated)
            throw new InvalidOperationException($"Video coverage plan '{coverage.Id}' is not validated.");
        return coverage;
    }

    private static IReadOnlyList<SceneBeatDialogueCue> SelectDialogue(
        SceneBeatProductionPlan plan,
        IReadOnlyList<string> ids,
        bool requireAny)
    {
        var selected = SelectExact(plan.DialogueCues, cue => cue.Id, ids, "dialogue/narration cue", requireAny);
        if (selected.Any(cue => cue.ReviewStatus != ProductionReviewStatus.Validated))
            throw new InvalidOperationException("Every selected dialogue/narration cue must be validated.");
        return selected;
    }

    private static IReadOnlyList<SceneBeatSoundCue> SelectSound(
        SceneBeatProductionPlan plan,
        IReadOnlyList<string> ids,
        bool requireAny)
    {
        var selected = SelectExact(plan.SoundCues, cue => cue.Id, ids, "sound cue", requireAny);
        if (selected.Any(cue => cue.ReviewStatus != ProductionReviewStatus.Validated))
            throw new InvalidOperationException("Every selected sound cue must be validated.");
        return selected;
    }

    private static IReadOnlyList<T> SelectExact<T>(
        IReadOnlyList<T> source,
        Func<T, string> getId,
        IReadOnlyList<string> ids,
        string label,
        bool requireAny)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (requireAny && ids.Count == 0)
            throw new InvalidOperationException($"At least one exact {label} id is required.");
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException($"Selected {label} ids must be non-empty and unique.");
        var result = new List<T>();
        foreach (var id in ids)
        {
            var matches = source.Where(item => string.Equals(getId(item), id, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"Selected {label} id '{id}' was absent or ambiguous.");
            result.Add(matches[0]);
        }
        return result;
    }

    private static IReadOnlyList<JsonElement> SelectMusicSections(string json, IReadOnlyList<string> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new InvalidOperationException("Selected music section keys must be non-empty and unique.");
        var root = ParseJson(json, "Music plan");
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Music plan must be a JSON array.");
        var sections = root.EnumerateArray().Select(section => section.Clone()).ToList();
        var result = new List<JsonElement>();
        foreach (var key in keys)
        {
            var matches = sections.Where(section =>
                section.ValueKind == JsonValueKind.Object &&
                section.TryGetProperty("sectionKey", out var property) &&
                string.Equals(property.GetString(), key, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"Selected music section key '{key}' was absent or ambiguous.");
            result.Add(matches[0]);
        }
        return result;
    }

    private static void ValidateSelectionMatchesCoverage(CompileMediaBriefRequest request, SceneVideoCoveragePlan coverage)
    {
        if (!request.DialogueCueIds.SequenceEqual(coverage.DialogueCueIds, StringComparer.Ordinal) ||
            !request.SoundCueIds.SequenceEqual(coverage.SoundCueIds, StringComparer.Ordinal) ||
            !request.MusicSectionKeys.SequenceEqual(coverage.MusicSectionKeys, StringComparer.Ordinal))
            throw new InvalidOperationException("Requested cue ids must exactly match the selected video coverage plan in canonical order.");
    }

    private static void RequireNoCoverage(CompileMediaBriefRequest request, MediaProductionKind kind)
    {
        if (request.VideoCoveragePlanId is not null)
            throw new InvalidOperationException($"{kind} compilation does not accept a video coverage plan id.");
    }

    private static JsonElement ParseJson(string json, string label)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"{label} contains invalid canonical JSON.", exception);
        }
    }

    private static string MusicCueId(string planId, JsonElement section)
    {
        if (!section.TryGetProperty("sectionKey", out var key) || string.IsNullOrWhiteSpace(key.GetString()))
            throw new InvalidOperationException("A selected music section is missing sectionKey.");
        return $"{planId}:{key.GetString()}";
    }

    private sealed record Projection(object SemanticInput, IReadOnlySet<MediaCompilerCapability> RequiredCapabilities);
}

public static class SceneVideoAudioOwnershipValidator
{
    private static readonly HashSet<string> AllowedOwnership =
        ["ExternalMix", "GeneratedWithVideo", "Hybrid", "None"];

    public static IReadOnlyList<SceneVideoCueAudioOwnership> Validate(SceneVideoCoveragePlan coverage)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        var referenced = coverage.DialogueCueIds.Concat(coverage.SoundCueIds)
            .Concat(coverage.MusicSectionKeys.Select(key => $"{coverage.BeatProductionPlanId}:{key}"))
            .ToList();
        if (referenced.Distinct(StringComparer.Ordinal).Count() != referenced.Count)
            throw new InvalidOperationException($"Video coverage '{coverage.Id}' contains duplicate cue references.");
        if (coverage.AudioOwnership.Any(item => !AllowedOwnership.Contains(item.OwnershipIntent)))
            throw new InvalidOperationException($"Video coverage '{coverage.Id}' contains an unknown audio ownership intent.");
        if (coverage.AudioOwnership.Select(item => item.CueId).Distinct(StringComparer.Ordinal).Count() != coverage.AudioOwnership.Count)
            throw new InvalidOperationException($"Video coverage '{coverage.Id}' contains duplicate or conflicting audio ownership entries.");
        if (!referenced.ToHashSet(StringComparer.Ordinal).SetEquals(
            coverage.AudioOwnership.Select(item => item.CueId)))
            throw new InvalidOperationException($"Video coverage '{coverage.Id}' requires exactly one ownership entry for every referenced cue.");
        return coverage.AudioOwnership.ToList();
    }
}