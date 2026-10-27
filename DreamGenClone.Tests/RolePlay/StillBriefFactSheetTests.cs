using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-167 Part B: the compile LLM is handed labelled FACTS, not the raw brief JSON, and an operator-edited value is
/// marked as intent to translate. These are the properties the caption's shape depends on.
/// </summary>
public sealed class StillBriefFactSheetTests
{
    private const string Snapshot = """
        {
          "lineage": { "catalogueId": "cat1" },
          "moment": { "momentId": "m1", "temporalAnchor": "the shared look", "compositionRationale": "wide two-shot" },
          "frozenState": {
            "location": "the shed behind the trailers",
            "timeOfDay": "morning",
            "lighting": "barred slat light",
            "environment": "grit in the air",
            "mood": "charged",
            "objects": ["workbench", "deck"],
            "event": { "anchor": "workbench", "line": "Dean bends Becky over the workbench." },
            "contacts": [ { "from": "p1", "part": "hands", "to": "p0", "targetPart": "hips", "manner": "grip" } ],
            "characters": [
              { "profileKey": "p0", "characterId": "c0", "name": "Becky", "involvement": "active",
                "physicalLocation": "inside the shed", "position": "bent over the boards",
                "actionOrObservation": "bracing on the boards", "sightline": "down at the boards",
                "visibleCharacterNames": ["Dean"], "clothing": "nude",
                "at": { "anchor": "workbench", "posture": "bent forward" }, "facing": { "toward": "the boards" } }
            ],
            "continuityState": "stable"
          },
          "blocking": {
            "profileVersion": "2",
            "subjects": [ { "subjectKey": "p0", "position": { "horizontal": "Center", "depth": "Foreground" }, "roleText": "the person in the CENTER in the foreground" } ],
            "camera": { "angle": "eye level", "shotSize": "medium shot", "side": null },
            "layering": ["p0"],
            "rationale": "wide two-shot"
          },
          "typedReferences": []
        }
        """;

    /// <summary>
    /// The contact line names PEOPLE, never the profile keys the record joins on. Live leak 2026-10-25 (SceneImage
    /// 57fcc461): the caption reproduced "buried to the root (p0)" because the fact sheet printed the raw key — and a
    /// parenthesised token is weight syntax in a diffusion prompt, so it is not merely noise.
    /// </summary>
    [Fact]
    public void Render_ResolvesContactKeysToRecordedNames()
    {
        const string snapshot = """
            {
              "moment": { "momentId": "m1" },
              "frozenState": {
                "location": "the shed",
                "characters": [
                  { "profileKey": "p0", "characterId": "c0", "name": "Becky", "involvement": "active", "clothing": "nude" },
                  { "profileKey": "p1", "characterId": "c1", "name": "Dean", "involvement": "active", "clothing": "nude" }
                ],
                "contacts": [
                  { "from": "p1", "part": "penis", "to": "p0", "targetPart": "vagina", "manner": "buried to the root" },
                  { "from": "p0", "part": "teeth", "to": "p0", "targetPart": "forearm", "manner": "sunk into" }
                ]
              },
              "blocking": { "subjects": [], "camera": { "angle": "high angle" } },
              "typedReferences": []
            }
            """;

        var sheet = StillBriefFactSheet.Render(snapshot, null);

        Assert.Contains("Dean's penis on Becky's vagina (buried to the root)", sheet, StringComparison.Ordinal);
        Assert.Contains("Becky's teeth on own forearm (sunk into)", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("p0", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ResolvesReferenceSubjectsAndFacingTargetsToWordsNeverKeys()
    {
        // Live 2026-10-27 (prompt record 8655924e): the fact sheet printed "REFERENCES - p1" and the projection's
        // world-facing fact printed "toward p0"; the small compiler copied both into the caption, where a profile key
        // is not a word (and "(p0)" is weight syntax). Every rendered value now passes the same key-to-word resolver.
        const string snapshot = """
            {
              "moment": { "momentId": "m1" },
              "frozenState": {
                "location": "the kitchen",
                "characters": [
                  { "profileKey": "p0", "name": "Becky", "involvement": "active",
                    "at": { "anchor": "floor", "posture": "kneeling" }, "facing": { "toward": "p1" } },
                  { "profileKey": "p1", "name": "Dean", "involvement": "active",
                    "at": { "anchor": "linoleum", "posture": "standing" }, "facing": { "toward": "toward p0" } }
                ]
              },
              "typedReferences": [
                { "referenceKey": "identity-p1", "role": "CharacterIdentity", "subjectKey": "p1" },
                { "referenceKey": "wardrobe-p1", "role": "WardrobeContinuity", "subjectKey": "p1" },
                { "referenceKey": "location", "role": "LocationContinuity", "subjectKey": null }
              ]
            }
            """;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["p1"] = "Dean" };
        var sheet = StillBriefFactSheet.Render(snapshot, null, viewpointProfileKey: "p0", namesByKey: names);

        Assert.Contains("facing: toward the camera", sheet, StringComparison.Ordinal);
        Assert.Contains("facing: Dean", sheet, StringComparison.Ordinal);
        Assert.Contains("the face of Dean", sheet, StringComparison.Ordinal);
        Assert.Contains("the wardrobe of Dean", sheet, StringComparison.Ordinal);
        Assert.Contains("the place", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("p0", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_StatesEachFactOnceUnderALabel()
    {
        var sheet = StillBriefFactSheet.Render(Snapshot, null);

        Assert.StartsWith(StillBriefFactSheet.Heading, sheet, StringComparison.Ordinal);
        Assert.Contains("PLACE: the shed behind the trailers", sheet, StringComparison.Ordinal);
        Assert.Contains("EVENT: Dean bends Becky over the workbench. (at workbench)", sheet, StringComparison.Ordinal);
        // The fixture records only p0 in its cast, so the contact's other end has no name to use — and a key is never
        // an option: it falls back to a person phrase.
        Assert.Contains("CONTACTS: the other person's hands on Becky's hips (grip)", sheet, StringComparison.Ordinal);
        // A profile key is an internal identifier; it must never reach the caption the compiler writes.
        Assert.DoesNotContain("p0", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("p1", sheet, StringComparison.Ordinal);
        Assert.Contains("body: bent forward at workbench", sheet, StringComparison.Ordinal);
        Assert.Contains("facing: the boards", sheet, StringComparison.Ordinal);
        Assert.Contains("camera: eye level, medium shot", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DoesNotSelectTheDroppedRestatements()
    {
        var sheet = StillBriefFactSheet.Render(Snapshot, null);

        // The global continuity state is in the snapshot here on purpose: the sheet must never restate it, because the
        // Still brief's one owner for the instant's prose is EVENT.
        Assert.DoesNotContain("stable", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("visualDescription", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("{", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MarksAnOperatorEditedValueAsIntent()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.location", Value = "unused-by-the-sheet" }]
        };

        var sheet = StillBriefFactSheet.Render(Snapshot, overrides);

        Assert.Contains("PLACE: the shed behind the trailers" + StillBriefFactSheet.IntentMarker, sheet, StringComparison.Ordinal);
        // A fact the operator did NOT touch carries no marker.
        Assert.DoesNotContain("MOOD: charged" + StillBriefFactSheet.IntentMarker, sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SaysNotRecordedWhenContactsAreAbsent()
    {
        var withoutContacts = Snapshot.Replace(
            "\"contacts\": [ { \"from\": \"p1\", \"part\": \"hands\", \"to\": \"p0\", \"targetPart\": \"hips\", \"manner\": \"grip\" } ],",
            string.Empty,
            StringComparison.Ordinal);

        var sheet = StillBriefFactSheet.Render(withoutContacts, null);

        // Absent is "not recorded" (a v3 moment). Claiming "none" would assert a fact the record does not contain.
        Assert.Contains("CONTACTS: not recorded", sheet, StringComparison.Ordinal);
        Assert.DoesNotContain("CONTACTS: none", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SaysNoneOnlyWhenTheRecordDeclaresNoContact()
    {
        var emptyContacts = Snapshot.Replace(
            "[ { \"from\": \"p1\", \"part\": \"hands\", \"to\": \"p0\", \"targetPart\": \"hips\", \"manner\": \"grip\" } ]",
            "[]",
            StringComparison.Ordinal);

        var sheet = StillBriefFactSheet.Render(emptyContacts, null);

        Assert.Contains("CONTACTS: none", sheet, StringComparison.Ordinal);
    }
}
