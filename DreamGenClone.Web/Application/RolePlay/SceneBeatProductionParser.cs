using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class SceneBeatProductionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public SceneBeatProductionPlanData Parse(
        string planId,
        string rawResponse,
        SceneBeatProductionSourceSnapshot snapshot)
    {
        Require(planId, "Beat Production Plan id");
        Require(rawResponse, "Beat Production response");
        ArgumentNullException.ThrowIfNull(snapshot);

        ProductionResponse response;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(rawResponse);
            response = JsonSerializer.Deserialize<ProductionResponse>(rawResponse, JsonOptions)
                ?? throw new InvalidOperationException("Beat Production response was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Beat Production returned malformed or contract-invalid JSON.", ex);
        }

        using (document)
        {
            if (response.SchemaVersion != SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion)
                throw new InvalidOperationException($"Beat Production returned unsupported schemaVersion {response.SchemaVersion}.");
            if (!string.Equals(response.CatalogueBeatId, snapshot.Beat.BeatId, StringComparison.Ordinal))
                throw new InvalidOperationException("Beat Production response does not match the selected Beat.");

            var resolver = new SceneBeatProductionSourceResolver(snapshot);
            ValidateUniqueOrdered(response.Events, item => item.EventKey, item => item.Order, "events", true);
            var eventKeys = response.Events.Select(item => item.EventKey).ToHashSet(StringComparer.Ordinal);
            foreach (var item in response.Events)
            {
                Require(item.Description, $"Event '{item.EventKey}' description");
                foreach (var key in item.EvidenceKeys) resolver.ValidateBeatEvidenceKey(key);
                ValidateWindow(item.Window, eventKeys, null, $"Event '{item.EventKey}' window");
            }

            ValidateWindow(response.Timeline.BeatWindow, eventKeys, null, "Beat timeline window");
            var beatWindow = response.Timeline.BeatWindow;
            var dialogueInputs = response.Narration.Concat(response.Dialogue).ToList();
            ValidateUniqueOrdered(dialogueInputs, item => item.CueKey, item => item.Order, "dialogue and narration cues");
            var dialogue = dialogueInputs.Select(item => ParseDialogue(planId, item, resolver, eventKeys, beatWindow)).ToList();

            ValidateAmbience(response.Ambience, eventKeys, beatWindow, snapshot.Beat.PrimaryLocation);
            ValidateUniqueOrdered(response.SoundEvents, item => item.CueKey, item => item.Order, "sound cues");
            var sound = new List<SceneBeatSoundCue> { ParseAmbience(planId, response.Ambience) };
            sound.AddRange(response.SoundEvents.Select(item => ParseSound(planId, item, resolver, eventKeys, beatWindow)));

            ValidateUniqueOrdered(response.Music, item => item.SectionKey, item => item.Order, "music sections");
            foreach (var section in response.Music)
            {
                Require(section.Mood, $"Music section '{section.SectionKey}' mood");
                if (!section.Instrumental)
                    throw new InvalidOperationException($"Music section '{section.SectionKey}' cannot introduce unauthored lyrics.");
                ValidateWindow(section.Window, eventKeys, beatWindow, $"Music section '{section.SectionKey}' window");
            }

            ValidateUniqueOrdered(response.ActionArc, _ => string.Empty, item => item.Order, "action steps", uniqueKeys: false);
            foreach (var step in response.ActionArc)
            {
                RequireEvent(step.EventKey, eventKeys, "Action step");
                resolver.ResolveCharacterId(step.SubjectKey);
                if (step.TargetKey is not null) resolver.ResolveCharacterId(step.TargetKey);
                Require(step.Action, "Action");
                Require(step.ResultingState, "Action resulting state");
            }

            var startContinuity = ParseContinuity(response.StartContinuity, resolver, "start continuity");
            var endContinuity = ParseContinuity(response.EndContinuity, resolver, "end continuity");

            RequireUnique(response.TypedReferences, item => item.ReferenceKey, "typed reference keys");
            var references = response.TypedReferences.Select(item => ParseReference(item, resolver, eventKeys, beatWindow)).ToList();
            var referencesByKey = response.TypedReferences.Zip(references).ToDictionary(pair => pair.First.ReferenceKey, pair => pair.Second, StringComparer.Ordinal);

            ValidateUniqueOrdered(response.VideoCoverage, item => item.CoverageKey, _ => 0, "video coverage", ordered: false);
            var dialogueByKey = dialogueInputs.Zip(dialogue).ToDictionary(pair => pair.First.CueKey, pair => pair.Second, StringComparer.Ordinal);
            var soundByKey = response.SoundEvents.Zip(sound.Skip(1)).ToDictionary(pair => pair.First.CueKey, pair => pair.Second, StringComparer.Ordinal);
            var musicKeys = response.Music.Select(item => item.SectionKey).ToHashSet(StringComparer.Ordinal);
            var videos = response.VideoCoverage.Select(item => ParseVideo(
                planId, item, eventKeys, beatWindow, referencesByKey, dialogueByKey, soundByKey, musicKeys)).ToList();

            var root = document.RootElement;
            return new SceneBeatProductionPlanData(
                SerializeSection(root, "events"),
                SerializeSection(root, "timeline"),
                SerializeSection(root, "narration"),
                SerializeSection(root, "dialogue"),
                SerializeSection(root, "ambience"),
                SerializeSection(root, "soundEvents"),
                SerializeSection(root, "music"),
                SerializeSection(root, "actionArc"),
                SerializeSection(root, "startContinuity"),
                SerializeSection(root, "endContinuity"),
                SerializeSection(root, "typedReferences"),
                SerializeSection(root, "videoCoverage"),
                dialogue,
                sound,
                videos);
        }
    }

    private static SceneBeatDialogueCue ParseDialogue(
        string planId,
        DialogueInput item,
        SceneBeatProductionSourceResolver resolver,
        HashSet<string> eventKeys,
        WindowInput beatWindow)
    {
        RequireEvent(item.EventKey, eventKeys, $"Dialogue cue '{item.CueKey}'");
        var span = resolver.ResolveExactSpan(item.SourceKey, item.StartOffset, item.EndOffset, item.ExactSourceText);
        Require(item.DisplayText, $"Dialogue cue '{item.CueKey}' display text");
        ValidateSpokenNormalization(item.ExactSourceText, item.NormalizedSpokenText, item.CueKey);
        Require(item.NormalizationMethod, $"Dialogue cue '{item.CueKey}' normalization method");
        Require(item.NormalizationVersion, $"Dialogue cue '{item.CueKey}' normalization version");

        string? speakerId = null;
        if (item.Kind == SceneBeatDialogueKind.Narration)
        {
            if (item.SpeakerKey is not null)
                throw new InvalidOperationException($"Narration cue '{item.CueKey}' must not declare a speaker.");
            if (item.ReviewStatus == ProductionReviewStatus.ReviewRequired)
            {
                if (string.IsNullOrWhiteSpace(item.ReviewReason))
                    throw new InvalidOperationException($"Review-required narration cue '{item.CueKey}' requires a review reason.");
            }
            else if (item.ReviewReason is not null)
            {
                throw new InvalidOperationException($"Validated narration cue '{item.CueKey}' must not carry a review reason.");
            }
        }
        else if (item.ReviewStatus == ProductionReviewStatus.ReviewRequired)
        {
            if (item.SpeakerKey is not null || string.IsNullOrWhiteSpace(item.ReviewReason))
                throw new InvalidOperationException($"Review-required dialogue cue '{item.CueKey}' must have no speaker and a reason.");
        }
        else
        {
            if (item.SpeakerKey is null || item.ReviewReason is not null)
                throw new InvalidOperationException($"Validated dialogue cue '{item.CueKey}' requires one speaker and no review reason.");
            speakerId = resolver.ResolveCharacterId(item.SpeakerKey);
        }
        if (!string.Equals(item.Performance.SpeakerKey, item.SpeakerKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"Dialogue cue '{item.CueKey}' performance speaker does not match attribution.");
        var addresseeIds = resolver.ResolveCharacterIds(item.AddresseeKeys);
        ValidateWindow(item.Window, eventKeys, beatWindow, $"Dialogue cue '{item.CueKey}' window");
        Require(item.Performance.LanguageCode, $"Dialogue cue '{item.CueKey}' language");
        Require(item.Performance.Emotion, $"Dialogue cue '{item.CueKey}' emotion");
        Require(item.Performance.Intensity, $"Dialogue cue '{item.CueKey}' intensity");
        Require(item.Performance.Pace, $"Dialogue cue '{item.CueKey}' pace");

        return new SceneBeatDialogueCue(
            Id(planId, item.CueKey), planId, item.Order, item.Kind, item.EventKey,
            span.ExactText, item.DisplayText, item.NormalizedSpokenText,
            item.NormalizationMethod, item.NormalizationVersion, span.InteractionId,
            span.StartOffset, span.EndOffset, speakerId, addresseeIds,
            new VoicePerformanceIntent(
                speakerId, item.Performance.LanguageCode, item.Performance.Locale,
                item.Performance.Emotion, item.Performance.Intensity, item.Performance.Pace,
                item.Performance.AccentIntent, item.Performance.PauseCues,
                item.Performance.OverlapOrInterruption,
                item.Performance.PronunciationLexemes.Select(value => new VoicePronunciationLexeme(
                    value.SourceText, value.Pronunciation, value.Alphabet)).ToList(),
                item.Performance.NonVerbalVocalEvents),
            ToWindow(item.Window), item.LipSyncRelevant, item.ReviewStatus, item.ReviewReason);
    }

    private static void ValidateSpokenNormalization(string source, string spoken, string cueKey)
    {
        Require(spoken, $"Dialogue cue '{cueKey}' normalized spoken text");
        static string SemanticText(string value) => new(value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')
            .ToArray());
        var sourceWords = SemanticText(source).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spokenWords = SemanticText(spoken).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!sourceWords.SequenceEqual(spokenWords, StringComparer.Ordinal))
            throw new InvalidOperationException($"Dialogue cue '{cueKey}' normalized spoken text changes semantic words.");
    }

    private static void ValidateAmbience(
        AmbienceInput ambience,
        HashSet<string> eventKeys,
        WindowInput beatWindow,
        string beatLocation)
    {
        Require(ambience.Location, "Ambience location");
        if (!string.Equals(ambience.Location, beatLocation, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ambience location must match the selected Beat location.");
        Require(ambience.TimeContext, "Ambience time context");
        Require(ambience.IntensityEnvelope, "Ambience intensity envelope");
        Require(ambience.SpatialIntent, "Ambience spatial intent");
        Require(ambience.ContinuityIntent, "Ambience continuity intent");
        if (ambience.AuthoredSilence && ambience.SoundSources.Count > 0)
            throw new InvalidOperationException("Authored-silence ambience cannot also declare sound sources.");
        if (!ambience.AuthoredSilence && ambience.SoundSources.Count == 0)
            throw new InvalidOperationException("Non-silent ambience requires at least one sound source.");
        ValidateWindow(ambience.Window, eventKeys, beatWindow, "Ambience window");
    }

    private static SceneBeatSoundCue ParseAmbience(string planId, AmbienceInput item)
        => new(
            Id(planId, "ambience"), planId, 1, SceneBeatSoundKind.Ambience, null,
            item.Location, null, null,
            item.AuthoredSilence ? "Authored silence" : string.Join("; ", item.SoundSources),
            item.IntensityEnvelope, true, item.SpatialIntent, ToWindow(item.Window), true,
            null, item.ContinuityIntent, ProductionReviewStatus.Validated, null);

    private static SceneBeatSoundCue ParseSound(
        string planId,
        SoundInput item,
        SceneBeatProductionSourceResolver resolver,
        HashSet<string> eventKeys,
        WindowInput beatWindow)
    {
        if (item.EventKey is null && item.Window.StartSeconds is null && item.Window.StartEventKey is null)
            throw new InvalidOperationException($"Sound cue '{item.CueKey}' requires an event or Beat-relative anchor.");
        if (item.EventKey is not null) RequireEvent(item.EventKey, eventKeys, $"Sound cue '{item.CueKey}'");
        var subjectId = item.SubjectKey is null ? null : resolver.ResolveCharacterId(item.SubjectKey);
        ValidateWindow(item.Window, eventKeys, beatWindow, $"Sound cue '{item.CueKey}' window");
        ValidateReview(item.ReviewStatus, item.ReviewReason, $"Sound cue '{item.CueKey}'");
        return new SceneBeatSoundCue(
            Id(planId, item.CueKey), planId, item.Order + 1, item.Kind, item.EventKey,
            item.LocationSource, subjectId, item.ObjectReference, item.Description,
            item.IntensityEnvelope, item.Diegetic, item.SpatialIntent, ToWindow(item.Window),
            item.Loop, item.StemIntent, item.ContinuityGroup, item.ReviewStatus, item.ReviewReason);
    }

    private static SceneBeatContinuityState ParseContinuity(
        ContinuityInput item,
        SceneBeatProductionSourceResolver resolver,
        string context)
    {
        Require(item.Location, $"{context} location");
        Require(item.Lighting, $"{context} lighting");
        Require(item.StateSummary, $"{context} state summary");
        foreach (var state in item.CharacterStates) resolver.ResolveCharacterId(state.Key);
        foreach (var state in item.WardrobeStates) resolver.ResolveCharacterId(state.Key);
        return new SceneBeatContinuityState(
            item.Location,
            Dictionary(item.CharacterStates, $"{context} character states"),
            Dictionary(item.WardrobeStates, $"{context} wardrobe states"),
            Dictionary(item.ObjectStates, $"{context} object states"),
            item.Lighting,
            item.StateSummary);
    }

    private static TypedMediaReference ParseReference(
        ReferenceInput item,
        SceneBeatProductionSourceResolver resolver,
        HashSet<string> eventKeys,
        WindowInput beatWindow)
    {
        var subjectId = item.SubjectKey is null ? null : resolver.ResolveCharacterId(item.SubjectKey);
        if (item.Window is not null) ValidateWindow(item.Window, eventKeys, beatWindow, $"Reference '{item.ReferenceKey}' window");
        if (item.SourceRecordId is not null || item.AssetId is not null)
            throw new InvalidOperationException($"Reference '{item.ReferenceKey}' cannot invent source records or assets during analysis.");
        return new TypedMediaReference(
            item.ReferenceKey, item.Role, item.MediaKind, null, null, subjectId,
            item.Window is null ? null : ToWindow(item.Window), item.Required);
    }

    private static SceneVideoCoveragePlan ParseVideo(
        string planId,
        VideoInput item,
        HashSet<string> eventKeys,
        WindowInput beatWindow,
        IReadOnlyDictionary<string, TypedMediaReference> references,
        IReadOnlyDictionary<string, SceneBeatDialogueCue> dialogue,
        IReadOnlyDictionary<string, SceneBeatSoundCue> sound,
        HashSet<string> musicKeys)
    {
        ValidateWindow(item.Window, eventKeys, beatWindow, $"Video coverage '{item.CoverageKey}' window");
        foreach (var key in item.SourceEventKeys) RequireEvent(key, eventKeys, $"Video coverage '{item.CoverageKey}'");
        RequireKnown(item.ReferenceKeys, references.Keys, "reference", item.CoverageKey);
        RequireKnown(item.DialogueCueKeys, dialogue.Keys, "dialogue cue", item.CoverageKey);
        RequireKnown(item.SoundCueKeys, sound.Keys, "sound cue", item.CoverageKey);
        RequireKnown(item.MusicSectionKeys, musicKeys, "music section", item.CoverageKey);
        ValidateVideoRoles(item);
        ValidateReview(item.ReviewStatus, item.ReviewReason, $"Video coverage '{item.CoverageKey}'");

        var allCueKeys = item.DialogueCueKeys.Concat(item.SoundCueKeys).Concat(item.MusicSectionKeys).ToList();
        RequireUnique(item.AudioOwnership, value => value.CueKey, $"video coverage '{item.CoverageKey}' audio ownership");
        if (!allCueKeys.Order().SequenceEqual(item.AudioOwnership.Select(value => value.CueKey).Order(), StringComparer.Ordinal))
            throw new InvalidOperationException($"Video coverage '{item.CoverageKey}' requires exactly one audio ownership entry per referenced cue.");
        if (item.LipSyncRequired
            && (item.DialogueCueKeys.Count == 0 || item.DialogueCueKeys.Any(key => !dialogue[key].LipSyncRelevant)))
            throw new InvalidOperationException($"Video coverage '{item.CoverageKey}' lip sync requires lip-sync-relevant dialogue cues.");

        return new SceneVideoCoveragePlan(
            Id(planId, item.CoverageKey), planId, item.CoverageKey, item.Kind, ToWindow(item.Window),
            item.SourceEventKeys, item.RequiredMomentRoles, item.PermittedActionPhases,
            item.CameraIntent, item.LensIntent, item.MotionIntent, item.PacingIntent,
            item.ReferenceKeys.Select(key => references[key]).ToList(),
            item.DialogueCueKeys.Select(key => dialogue[key].Id).ToList(),
            item.SoundCueKeys.Select(key => sound[key].Id).ToList(), item.MusicSectionKeys,
            item.AudioOwnership.Select(value => new SceneVideoCueAudioOwnership(
                ResolveCueId(planId, value.CueKey, dialogue, sound, musicKeys), value.OwnershipIntent)).ToList(),
            item.LipSyncRequired, item.PerformanceIntent, item.DurationFitPolicy,
            item.ReviewStatus, item.ReviewReason);
    }

    private static string ResolveCueId(
        string planId,
        string key,
        IReadOnlyDictionary<string, SceneBeatDialogueCue> dialogue,
        IReadOnlyDictionary<string, SceneBeatSoundCue> sound,
        HashSet<string> musicKeys)
        => dialogue.TryGetValue(key, out var dialogueCue) ? dialogueCue.Id
            : sound.TryGetValue(key, out var soundCue) ? soundCue.Id
            : musicKeys.Contains(key) ? Id(planId, key)
            : throw new InvalidOperationException($"Unknown audio ownership cue '{key}'.");

    private static void ValidateVideoRoles(VideoInput item)
    {
        var required = item.Kind switch
        {
            SceneVideoCoverageKind.MomentHold => new[] { "start" },
            SceneVideoCoverageKind.MomentAction => new[] { "start", "end" },
            SceneVideoCoverageKind.MomentTransition => new[] { "start", "end" },
            SceneVideoCoverageKind.BeatExcerpt => new[] { "start", "end" },
            SceneVideoCoverageKind.WholeBeat => new[] { "start", "end" },
            _ => throw new InvalidOperationException($"Unsupported video coverage kind '{item.Kind}'.")
        };
        if (required.Any(role => !item.RequiredMomentRoles.Contains(role, StringComparer.Ordinal)))
            throw new InvalidOperationException($"Video coverage '{item.CoverageKey}' is missing required key-state roles.");
    }

    private static void ValidateWindow(
        WindowInput item,
        HashSet<string> eventKeys,
        WindowInput? beatWindow,
        string context)
    {
        Require(item.DurationIntent, $"{context} duration intent");
        if (item.StartSeconds is null && item.EndSeconds is null
            && item.StartEventKey is null && item.EndEventKey is null)
            throw new InvalidOperationException($"{context} requires a resolvable anchor.");
        if (item.StartSeconds < 0 || item.EndSeconds < 0
            || (item.StartSeconds is not null && item.EndSeconds is not null && item.EndSeconds < item.StartSeconds))
            throw new InvalidOperationException($"{context} has invalid Beat-relative seconds.");
        if (item.StartEventKey is not null) RequireEvent(item.StartEventKey, eventKeys, context);
        if (item.EndEventKey is not null) RequireEvent(item.EndEventKey, eventKeys, context);
        if (beatWindow?.StartSeconds is decimal beatStart && item.StartSeconds is decimal start
            && start < beatStart && !item.ContinuityLeadIn)
            throw new InvalidOperationException($"{context} starts outside the Beat window.");
        if (beatWindow?.EndSeconds is decimal beatEnd && item.EndSeconds is decimal end
            && end > beatEnd && !item.ContinuityTail)
            throw new InvalidOperationException($"{context} ends outside the Beat window.");
    }

    private static ProductionTimeWindow ToWindow(WindowInput item)
        => new(item.StartSeconds, item.EndSeconds, item.StartEventKey, item.EndEventKey,
            item.DurationIntent, item.Precision, item.OverlapPolicy, item.ContinuityLeadIn, item.ContinuityTail);

    private static IReadOnlyDictionary<string, string> Dictionary(IReadOnlyList<KeyValueInput> values, string context)
    {
        RequireUnique(values, item => item.Key, context);
        if (values.Any(item => string.IsNullOrWhiteSpace(item.Value)))
            throw new InvalidOperationException($"{context} values must be non-empty.");
        return values.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
    }

    private static void ValidateReview(ProductionReviewStatus status, string? reason, string context)
    {
        if ((status == ProductionReviewStatus.ReviewRequired) != !string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException($"{context} review status and reason are inconsistent.");
    }

    private static void RequireEvent(string key, HashSet<string> eventKeys, string context)
    {
        if (!eventKeys.Contains(key)) throw new InvalidOperationException($"{context} references unknown event '{key}'.");
    }

    private static void RequireKnown(IEnumerable<string> keys, IEnumerable<string> known, string kind, string coverageKey)
    {
        var knownSet = known.ToHashSet(StringComparer.Ordinal);
        var unknown = keys.Where(key => !knownSet.Contains(key)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Video coverage '{coverageKey}' references unknown {kind}: {string.Join(", ", unknown)}.");
    }

    private static void ValidateUniqueOrdered<T>(
        IReadOnlyList<T> values,
        Func<T, string> key,
        Func<T, int> order,
        string context,
        bool requireValues = false,
        bool uniqueKeys = true,
        bool ordered = true)
    {
        if (requireValues && values.Count == 0) throw new InvalidOperationException($"Beat Production requires {context}.");
        if (uniqueKeys) RequireUnique(values, key, context);
        if (ordered && !values.Select(order).SequenceEqual(Enumerable.Range(1, values.Count)))
            throw new InvalidOperationException($"Beat Production {context} must have contiguous positive order.");
    }

    private static void RequireUnique<T>(IReadOnlyList<T> values, Func<T, string> key, string context)
    {
        var keys = values.Select(key).ToList();
        if (keys.Any(string.IsNullOrWhiteSpace) || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
            throw new InvalidOperationException($"Beat Production {context} must be non-empty and unique.");
    }

    private static string SerializeSection(JsonElement root, string name)
        => root.GetProperty(name).GetRawText();

    private static string Id(string planId, string key) => $"{planId}:{key}";

    private static void Require(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"{name} is required.");
    }

    private sealed class ProductionResponse
    {
        public required int SchemaVersion { get; init; }
        public required string CatalogueBeatId { get; init; }
        public required List<EventInput> Events { get; init; }
        public required TimelineInput Timeline { get; init; }
        public required List<DialogueInput> Narration { get; init; }
        public required List<DialogueInput> Dialogue { get; init; }
        public required AmbienceInput Ambience { get; init; }
        public required List<SoundInput> SoundEvents { get; init; }
        public required List<MusicInput> Music { get; init; }
        public required List<ActionInput> ActionArc { get; init; }
        public required ContinuityInput StartContinuity { get; init; }
        public required ContinuityInput EndContinuity { get; init; }
        public required List<ReferenceInput> TypedReferences { get; init; }
        public required List<VideoInput> VideoCoverage { get; init; }
    }

    private sealed class EventInput { public required string EventKey { get; init; } public required int Order { get; init; } public required string Description { get; init; } public required List<string> EvidenceKeys { get; init; } public required WindowInput Window { get; init; } }
    private sealed class TimelineInput { public required string DurationIntent { get; init; } public required WindowInput BeatWindow { get; init; } }
    private sealed class DialogueInput
    {
        public required string CueKey { get; init; } public required int Order { get; init; } public required SceneBeatDialogueKind Kind { get; init; }
        public required string EventKey { get; init; } public required string ExactSourceText { get; init; } public required string DisplayText { get; init; }
        public required string NormalizedSpokenText { get; init; } public required string NormalizationMethod { get; init; } public required string NormalizationVersion { get; init; }
        public required string SourceKey { get; init; } public required int StartOffset { get; init; } public required int EndOffset { get; init; }
        public required string? SpeakerKey { get; init; } public required List<string> AddresseeKeys { get; init; } public required PerformanceInput Performance { get; init; }
        public required WindowInput Window { get; init; } public required bool LipSyncRelevant { get; init; } public required ProductionReviewStatus ReviewStatus { get; init; }
        public required string? ReviewReason { get; init; }
    }
    private sealed class PerformanceInput
    {
        public required string? SpeakerKey { get; init; } public required string LanguageCode { get; init; } public required string? Locale { get; init; }
        public required string Emotion { get; init; } public required string Intensity { get; init; } public required string Pace { get; init; }
        public required string? AccentIntent { get; init; } public required List<string> PauseCues { get; init; } public required string? OverlapOrInterruption { get; init; }
        public required List<PronunciationInput> PronunciationLexemes { get; init; } public required List<string> NonVerbalVocalEvents { get; init; }
    }
    private sealed class PronunciationInput { public required string SourceText { get; init; } public required string Pronunciation { get; init; } public required string? Alphabet { get; init; } }
    private sealed class AmbienceInput
    {
        public required string Location { get; init; } public required string TimeContext { get; init; } public required List<string> SoundSources { get; init; }
        public required string IntensityEnvelope { get; init; } public required string SpatialIntent { get; init; } public required bool AuthoredSilence { get; init; }
        public required string ContinuityIntent { get; init; } public required WindowInput Window { get; init; }
    }
    private sealed class SoundInput
    {
        public required string CueKey { get; init; } public required int Order { get; init; } public required SceneBeatSoundKind Kind { get; init; }
        public required string? EventKey { get; init; } public required string? LocationSource { get; init; } public required string? SubjectKey { get; init; }
        public required string? ObjectReference { get; init; } public required string Description { get; init; } public required string IntensityEnvelope { get; init; }
        public required bool Diegetic { get; init; } public required string SpatialIntent { get; init; } public required WindowInput Window { get; init; }
        public required bool Loop { get; init; } public required string? StemIntent { get; init; } public required string ContinuityGroup { get; init; }
        public required ProductionReviewStatus ReviewStatus { get; init; } public required string? ReviewReason { get; init; }
    }
    private sealed class MusicInput
    {
        public required string SectionKey { get; init; } public required int Order { get; init; } public required string Mood { get; init; }
        public required List<string> Instrumentation { get; init; } public required decimal? TempoBpm { get; init; } public required string? MusicalKey { get; init; }
        public required string TransitionIntent { get; init; } public required bool Instrumental { get; init; } public required string ContinuityIntent { get; init; }
        public required WindowInput Window { get; init; }
    }
    private sealed class ActionInput { public required int Order { get; init; } public required string EventKey { get; init; } public required string SubjectKey { get; init; } public required string Action { get; init; } public required string? TargetKey { get; init; } public required string? TargetObject { get; init; } public required string ResultingState { get; init; } }
    private sealed class ContinuityInput { public required string Location { get; init; } public required List<KeyValueInput> CharacterStates { get; init; } public required List<KeyValueInput> WardrobeStates { get; init; } public required List<KeyValueInput> ObjectStates { get; init; } public required string Lighting { get; init; } public required string StateSummary { get; init; } }
    private sealed class KeyValueInput { public required string Key { get; init; } public required string Value { get; init; } }
    private sealed class ReferenceInput
    {
        public required string ReferenceKey { get; init; } public required TypedMediaReferenceRole Role { get; init; } public required string MediaKind { get; init; }
        public required string? SourceRecordId { get; init; } public required string? AssetId { get; init; } public required string? SubjectKey { get; init; }
        public required WindowInput? Window { get; init; } public required bool Required { get; init; }
    }
    private sealed class VideoInput
    {
        public required string CoverageKey { get; init; } public required SceneVideoCoverageKind Kind { get; init; } public required WindowInput Window { get; init; }
        public required List<string> SourceEventKeys { get; init; } public required List<string> RequiredMomentRoles { get; init; } public required List<string> PermittedActionPhases { get; init; }
        public required string CameraIntent { get; init; } public required string LensIntent { get; init; } public required string MotionIntent { get; init; } public required string PacingIntent { get; init; }
        public required List<string> ReferenceKeys { get; init; } public required List<string> DialogueCueKeys { get; init; } public required List<string> SoundCueKeys { get; init; }
        public required List<string> MusicSectionKeys { get; init; } public required List<AudioOwnershipInput> AudioOwnership { get; init; } public required bool LipSyncRequired { get; init; }
        public required string PerformanceIntent { get; init; } public required string DurationFitPolicy { get; init; } public required ProductionReviewStatus ReviewStatus { get; init; }
        public required string? ReviewReason { get; init; }
    }
    private sealed class AudioOwnershipInput { public required string CueKey { get; init; } public required string OwnershipIntent { get; init; } }
    private sealed class WindowInput
    {
        public required decimal? StartSeconds { get; init; } public required decimal? EndSeconds { get; init; } public required string? StartEventKey { get; init; }
        public required string? EndEventKey { get; init; } public required string DurationIntent { get; init; } public required ProductionWindowPrecision Precision { get; init; }
        public required ProductionOverlapPolicy OverlapPolicy { get; init; } public required bool ContinuityLeadIn { get; init; } public required bool ContinuityTail { get; init; }
    }
}