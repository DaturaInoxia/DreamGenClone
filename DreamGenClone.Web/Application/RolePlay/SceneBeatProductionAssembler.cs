using System.Text.Json;
using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Deterministically assembles the final Beat Production plan JSON from the three structured passes
/// (structure, spoken, continuity). The LLM authors the semantic sections; this class computes the
/// mechanical sections — typed references, video coverage, and a default authored-silence soundscape —
/// that previously forced a large, fragile assembly LLM pass and a separate soundscape LLM pass. See
/// specs/Planning/B-100-progressive-scene-beat-pipeline/beat-production-deterministic-assembly-design.md.
/// </summary>
public static class SceneBeatProductionAssembler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static JsonObject Assemble(
        SceneBeatProductionSourceSnapshot snapshot,
        JsonObject structure,
        JsonObject spoken,
        JsonObject continuity)
    {
        var events = RequireArray(structure, "events", "structure");
        var timeline = Require(structure, "timeline", "structure");
        var actionArc = RequireArray(structure, "actionArc", "structure");
        var narration = ExpandSpokenCues(RequireArray(spoken, "narration", "spoken"));
        var dialogue = ExpandSpokenCues(RequireArray(spoken, "dialogue", "spoken"));
        var beatWindow = timeline["beatWindow"]!.DeepClone();
        var (ambience, soundEvents, music) = BuildDefaultSoundscape(snapshot, beatWindow);
        var startContinuity = Require(continuity, "startContinuity", "continuity");
        var endContinuity = Require(continuity, "endContinuity", "continuity");

        var typedReferences = BuildTypedReferences(snapshot, dialogue);
        var videoCoverage = BuildVideoCoverage(events, timeline, narration, dialogue, soundEvents, music, typedReferences);

        return new JsonObject
        {
            ["schemaVersion"] = SceneBeatProductionSnapshotBuilder.CurrentSchemaVersion,
            ["catalogueBeatId"] = snapshot.Beat.BeatId,
            ["events"] = events.DeepClone(),
            ["timeline"] = timeline.DeepClone(),
            ["narration"] = narration.DeepClone(),
            ["dialogue"] = dialogue.DeepClone(),
            ["ambience"] = ambience.DeepClone(),
            ["soundEvents"] = soundEvents.DeepClone(),
            ["music"] = music.DeepClone(),
            ["actionArc"] = actionArc.DeepClone(),
            ["startContinuity"] = startContinuity.DeepClone(),
            ["endContinuity"] = endContinuity.DeepClone(),
            ["typedReferences"] = typedReferences,
            ["videoCoverage"] = videoCoverage
        };
    }

    private static JsonArray ExpandSpokenCues(JsonArray cues)
    {
        var expanded = new JsonArray();
        foreach (var node in cues)
        {
            var cue = (JsonObject)node!;
            var exact = cue["exactSourceText"]!.GetValue<string>();
            var eventKey = cue["eventKey"]!.GetValue<string>();
            var speakerKey = cue["speakerKey"] is JsonValue sv ? sv.GetValue<string>() : null;
            var reviewReason = cue["reviewReason"] is JsonValue rv ? rv.GetValue<string>() : null;

            var performance = JsonSerializer.SerializeToElement(new
            {
                speakerKey = speakerKey,
                languageCode = "en-US",
                locale = (string?)null,
                emotion = "Neutral",
                intensity = "Medium",
                pace = "Medium",
                accentIntent = (string?)null,
                pauseCues = Array.Empty<string>(),
                overlapOrInterruption = (string?)null,
                pronunciationLexemes = Array.Empty<object>(),
                nonVerbalVocalEvents = Array.Empty<string>()
            }, JsonOptions);

            var window = JsonSerializer.SerializeToElement(new
            {
                startSeconds = (decimal?)null,
                endSeconds = (decimal?)null,
                startEventKey = eventKey,
                endEventKey = eventKey,
                durationIntent = "Beat",
                precision = "Exact",
                overlapPolicy = "Allow",
                continuityLeadIn = false,
                continuityTail = false
            }, JsonOptions);

            expanded.Add(Node(new
            {
                cueKey = cue["cueKey"]!.GetValue<string>(),
                order = cue["order"]!.GetValue<int>(),
                kind = cue["kind"]!.GetValue<string>(),
                eventKey = eventKey,
                exactSourceText = exact,
                displayText = exact,
                normalizedSpokenText = exact,
                normalizationMethod = "verbatim",
                normalizationVersion = "1",
                sourceKey = cue["sourceKey"]!.GetValue<string>(),
                speakerKey = speakerKey,
                addresseeKeys = Array.Empty<string>(),
                performance = performance,
                window = window,
                lipSyncRelevant = false,
                reviewStatus = cue["reviewStatus"]!.GetValue<string>(),
                reviewReason = reviewReason
            }));
        }
        return expanded;
    }

    private static (JsonObject ambience, JsonArray soundEvents, JsonArray music) BuildDefaultSoundscape(
        SceneBeatProductionSourceSnapshot snapshot,
        JsonNode beatWindow)
    {
        var ambience = (JsonObject)Node(new
        {
            location = snapshot.Beat.PrimaryLocation,
            timeContext = "Beat duration",
            soundSources = Array.Empty<string>(),
            intensityEnvelope = "Silence",
            spatialIntent = "None",
            authoredSilence = true,
            continuityIntent = "None",
            window = JsonSerializer.SerializeToElement(beatWindow, JsonOptions)
        })!;
        return (ambience, new JsonArray(), new JsonArray());
    }

    private static JsonArray BuildTypedReferences(SceneBeatProductionSourceSnapshot snapshot, JsonArray dialogue)
    {
        var references = new JsonArray();
        foreach (var participant in snapshot.Beat.Participants)
        {
            references.Add(Node(new
            {
                referenceKey = $"identity-{participant.ProfileKey}",
                role = "CharacterIdentity",
                mediaKind = "Identity",
                sourceRecordId = (string?)null,
                assetId = (string?)null,
                subjectKey = participant.ProfileKey,
                window = (object?)null,
                required = true
            }));
            references.Add(Node(new
            {
                referenceKey = $"wardrobe-{participant.ProfileKey}",
                role = "WardrobeContinuity",
                mediaKind = "Wardrobe",
                sourceRecordId = (string?)null,
                assetId = (string?)null,
                subjectKey = participant.ProfileKey,
                window = (object?)null,
                required = true
            }));
        }
        references.Add(Node(new
        {
            referenceKey = "location",
            role = "LocationContinuity",
            mediaKind = "Location",
            sourceRecordId = (string?)null,
            assetId = (string?)null,
            subjectKey = (string?)null,
            window = (object?)null,
            required = true
        }));

        var speakers = dialogue
            .OfType<JsonObject>()
            .Select(cue => cue["speakerKey"])
            .Where(node => node is JsonValue)
            .Select(node => node!.GetValue<string>())
            .Distinct(StringComparer.Ordinal);
        foreach (var speaker in speakers)
        {
            references.Add(Node(new
            {
                referenceKey = $"voice-{speaker}",
                role = "VoiceIdentity",
                mediaKind = "Voice",
                sourceRecordId = (string?)null,
                assetId = (string?)null,
                subjectKey = speaker,
                window = (object?)null,
                required = true
            }));
        }

        return references;
    }

    private static JsonArray BuildVideoCoverage(
        JsonArray events,
        JsonObject timeline,
        JsonArray narration,
        JsonArray dialogue,
        JsonArray soundEvents,
        JsonArray music,
        JsonArray typedReferences)
    {
        var eventKeys = events.OfType<JsonObject>().Select(item => item["eventKey"]!.GetValue<string>()).ToList();
        var cueKeys = narration.OfType<JsonObject>()
            .Concat(dialogue.OfType<JsonObject>())
            .Select(item => item["cueKey"]!.GetValue<string>())
            .ToList();
        var soundKeys = soundEvents.OfType<JsonObject>().Select(item => item["cueKey"]!.GetValue<string>()).ToList();
        var musicKeys = music.OfType<JsonObject>().Select(item => item["sectionKey"]!.GetValue<string>()).ToList();
        var referenceKeys = typedReferences.OfType<JsonObject>().Select(item => item["referenceKey"]!.GetValue<string>()).ToList();
        var beatWindow = timeline["beatWindow"]!.DeepClone();
        var beatWindowElement = JsonSerializer.SerializeToElement(beatWindow, JsonOptions);

        var ownership = new JsonArray();
        foreach (var key in cueKeys) ownership.Add(Node(new { cueKey = key, ownershipIntent = "Foreground" }));
        foreach (var key in soundKeys) ownership.Add(Node(new { cueKey = key, ownershipIntent = "Foreground" }));
        foreach (var key in musicKeys) ownership.Add(Node(new { cueKey = key, ownershipIntent = "Background" }));
        var ownershipElement = JsonSerializer.SerializeToElement(ownership, JsonOptions);

        var coverage = Node(new
        {
            coverageKey = "cov-whole",
            kind = "WholeBeat",
            window = beatWindowElement,
            sourceEventKeys = eventKeys,
            requiredMomentRoles = new[] { "start", "end" },
            permittedActionPhases = Array.Empty<string>(),
            cameraIntent = "Establish the selected Beat's location, participants, and action.",
            lensIntent = "Static wide establishing shots with selective close-ups on key actions.",
            motionIntent = "Minimal camera movement; motion comes from character action.",
            pacingIntent = "Match the narrative pacing of the selected Beat.",
            referenceKeys = referenceKeys,
            dialogueCueKeys = cueKeys,
            soundCueKeys = soundKeys,
            musicSectionKeys = musicKeys,
            audioOwnership = ownershipElement,
            lipSyncRequired = false,
            performanceIntent = "Neutral, source-faithful performance.",
            durationFitPolicy = "Exact",
            reviewStatus = "Validated",
            reviewReason = (string?)null
        });

        var coverageArray = new JsonArray();
        coverageArray.Add(coverage);
        return coverageArray;
    }

    private static JsonObject Require(JsonObject pass, string section, string passId)
        => pass.TryGetPropertyValue(section, out var node) && node is JsonObject obj
            ? obj
            : throw new InvalidOperationException($"Beat Production '{passId}' pass omitted required section '{section}'.");

    private static JsonArray RequireArray(JsonObject pass, string section, string passId)
        => pass.TryGetPropertyValue(section, out var node) && node is JsonArray array
            ? array
            : throw new InvalidOperationException($"Beat Production '{passId}' section '{section}' is not an array.");

    private static JsonNode? Node(object value) => JsonSerializer.SerializeToNode(value, JsonOptions);
}
