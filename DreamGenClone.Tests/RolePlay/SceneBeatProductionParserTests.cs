using System.Text.Json.Nodes;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatProductionParserTests
{
    internal const string ValidResponse = """
        {
          "schemaVersion": 1,
          "catalogueBeatId": "b1",
          "events": [
            {
              "eventKey": "e1", "order": 1, "description": "Dean speaks to Becky", "evidenceKeys": ["n0", "c1"],
              "window": { "startSeconds": 0, "endSeconds": 2, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "brief", "precision": "Estimated", "overlapPolicy": "Allow", "continuityLeadIn": false, "continuityTail": false }
            }
          ],
          "timeline": {
            "durationIntent": "brief exchange",
            "beatWindow": { "startSeconds": 0, "endSeconds": 4, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "four seconds", "precision": "Estimated", "overlapPolicy": "Disallow", "continuityLeadIn": false, "continuityTail": false }
          },
          "narration": [],
          "dialogue": [
            {
              "cueKey": "d1", "order": 1, "kind": "Dialogue", "eventKey": "e1",
              "exactSourceText": "You're still awake.", "displayText": "You're still awake.", "normalizedSpokenText": "You're still awake.",
              "normalizationMethod": "identity", "normalizationVersion": "1", "sourceKey": "c1", "startOffset": 0, "endOffset": 19,
              "speakerKey": "p1", "addresseeKeys": ["p0"],
              "performance": { "speakerKey": "p1", "languageCode": "en", "locale": null, "emotion": "quiet surprise", "intensity": "low", "pace": "measured", "accentIntent": null, "pauseCues": [], "overlapOrInterruption": null, "pronunciationLexemes": [], "nonVerbalVocalEvents": [] },
              "window": { "startSeconds": 1, "endSeconds": 2, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "one second", "precision": "Estimated", "overlapPolicy": "Allow", "continuityLeadIn": false, "continuityTail": false },
              "lipSyncRelevant": true, "reviewStatus": "Validated", "reviewReason": null
            }
          ],
          "ambience": {
            "location": "entry hall", "timeContext": "evening", "soundSources": ["low room tone"], "intensityEnvelope": "steady low", "spatialIntent": "surrounding", "authoredSilence": false, "continuityIntent": "continue through beat",
            "window": { "startSeconds": 0, "endSeconds": 4, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "whole beat", "precision": "Estimated", "overlapPolicy": "Allow", "continuityLeadIn": false, "continuityTail": false }
          },
          "soundEvents": [
            {
              "cueKey": "s1", "order": 1, "kind": "SoundEffect", "eventKey": "e1", "locationSource": "entry hall", "subjectKey": "p0", "objectReference": "floor", "description": "footstep", "intensityEnvelope": "brief", "diegetic": true, "spatialIntent": "center",
              "window": { "startSeconds": 0, "endSeconds": 1, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "brief", "precision": "Estimated", "overlapPolicy": "Allow", "continuityLeadIn": false, "continuityTail": false },
              "loop": false, "stemIntent": null, "continuityGroup": "hall-effects", "reviewStatus": "Validated", "reviewReason": null
            }
          ],
          "music": [],
          "actionArc": [
            { "order": 1, "eventKey": "e1", "subjectKey": "p0", "action": "turns toward", "targetKey": "p1", "targetObject": null, "resultingState": "Becky faces Dean" }
          ],
          "startContinuity": {
            "location": "entry hall", "characterStates": [{"key":"p0","value":"standing"},{"key":"p1","value":"seated"}], "wardrobeStates": [{"key":"p0","value":"blue shirt"}], "objectStates": [{"key":"door","value":"open"}], "lighting": "warm ceiling light", "stateSummary": "Becky has just entered"
          },
          "endContinuity": {
            "location": "entry hall", "characterStates": [{"key":"p0","value":"facing Dean"},{"key":"p1","value":"looking up"}], "wardrobeStates": [{"key":"p0","value":"blue shirt"}], "objectStates": [{"key":"door","value":"open"}], "lighting": "warm ceiling light", "stateSummary": "They face one another"
          },
          "typedReferences": [
            { "referenceKey": "r1", "role": "CharacterIdentity", "mediaKind": "Image", "sourceRecordId": null, "assetId": null, "subjectKey": "p0", "window": null, "required": true }
          ],
          "videoCoverage": [
            {
              "coverageKey": "v1", "kind": "MomentTransition",
              "window": { "startSeconds": 0, "endSeconds": 4, "startEventKey": "e1", "endEventKey": "e1", "durationIntent": "whole beat", "precision": "Estimated", "overlapPolicy": "Allow", "continuityLeadIn": false, "continuityTail": false },
              "sourceEventKeys": ["e1"], "requiredMomentRoles": ["start", "end"], "permittedActionPhases": ["turn"], "cameraIntent": "wide", "lensIntent": "normal", "motionIntent": "track", "pacingIntent": "measured", "referenceKeys": ["r1"], "dialogueCueKeys": ["d1"], "soundCueKeys": ["s1"], "musicSectionKeys": [],
              "audioOwnership": [{"cueKey":"d1","ownershipIntent":"ExternalMix"},{"cueKey":"s1","ownershipIntent":"ExternalMix"}],
              "lipSyncRequired": true, "performanceIntent": "preserve delivery", "durationFitPolicy": "fit-to-window", "reviewStatus": "Validated", "reviewReason": null
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ProjectsCanonicalDataAndAuthoritativeLineage()
    {
        var result = new SceneBeatProductionParser().Parse("plan-1", ValidResponse, CreateSnapshot());

        var dialogue = Assert.Single(result.DialogueCues);
        Assert.Equal("interaction-1", dialogue.SourceInteractionId);
        Assert.Equal("character-dean", dialogue.SpeakerCharacterId);
        Assert.Equal(["character-becky"], dialogue.AddresseeCharacterIds);
        Assert.Equal("You're still awake.", dialogue.ExactSourceText);
        Assert.Equal(2, result.SoundCues.Count);
        Assert.Equal(SceneBeatSoundKind.Ambience, result.SoundCues[0].Kind);
        var video = Assert.Single(result.VideoCoveragePlans);
        Assert.Equal(SceneVideoCoverageKind.MomentTransition, video.CoverageKind);
        Assert.Equal([dialogue.Id], video.DialogueCueIds);
        Assert.Equal(2, video.AudioOwnership.Count);
        Assert.Contains("\"eventKey\": \"e1\"", result.NarrativeArcJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsExactTextThatDoesNotMatchImmutableSpan()
    {
        var response = ValidResponse.Replace("You're still awake.\", \"displayText", "You're still asleep.\", \"displayText");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("exact source text does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsIncompleteVideoAudioOwnership()
    {
        var response = ValidResponse.Replace(
            "[{\"cueKey\":\"d1\",\"ownershipIntent\":\"ExternalMix\"},{\"cueKey\":\"s1\",\"ownershipIntent\":\"ExternalMix\"}]",
            "[{\"cueKey\":\"d1\",\"ownershipIntent\":\"ExternalMix\"}]");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("exactly one audio ownership", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_RejectsGuessedSpeakerOnReviewRequiredCue()
    {
        var response = ValidResponse
            .Replace("\"reviewStatus\": \"Validated\", \"reviewReason\": null", "\"reviewStatus\": \"ReviewRequired\", \"reviewReason\": \"speaker ambiguous\"", StringComparison.Ordinal)
            .Replace("\"reviewStatus\": \"ReviewRequired\", \"reviewReason\": \"speaker ambiguous\"", "\"reviewStatus\": \"ReviewRequired\", \"reviewReason\": \"speaker ambiguous\"", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("must have no speaker", error.Message, StringComparison.OrdinalIgnoreCase);
    }

      [Fact]
      public void Parse_PreservesAmbiguousAttributionAsReviewRequiredWithoutGuessingSpeaker()
      {
        var response = MutateResponse(root =>
        {
          var dialogue = root["dialogue"]![0]!;
          dialogue["speakerKey"] = null;
          dialogue["performance"]!["speakerKey"] = null;
          dialogue["reviewStatus"] = "ReviewRequired";
          dialogue["reviewReason"] = "speaker ambiguous in source";
        });

        var cue = Assert.Single(Parse(response).DialogueCues);

        Assert.Null(cue.SpeakerCharacterId);
        Assert.Equal(ProductionReviewStatus.ReviewRequired, cue.ReviewStatus);
        Assert.Equal("speaker ambiguous in source", cue.ReviewReason);
      }

      [Fact]
      public void Parse_AcceptsValidatedNarrationCueWithoutSpeaker()
      {
        var response = MutateResponse(root =>
        {
          var cue = root["dialogue"]![0]!;
          cue["kind"] = "Narration";
          cue["speakerKey"] = null;
          cue["performance"]!["speakerKey"] = null;
          cue["lipSyncRelevant"] = false;
          root["videoCoverage"]![0]!["lipSyncRequired"] = false;
        });

        var narration = Assert.Single(Parse(response).DialogueCues);

        Assert.Equal(SceneBeatDialogueKind.Narration, narration.Kind);
        Assert.Null(narration.SpeakerCharacterId);
        Assert.Equal(ProductionReviewStatus.Validated, narration.ReviewStatus);
      }

      [Fact]
      public void Parse_RejectsNarrationCueWithSpeaker()
      {
        var response = MutateResponse(root =>
        {
          var cue = root["dialogue"]![0]!;
          cue["kind"] = "Narration";
          cue["performance"]!["speakerKey"] = null;
        });

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("must not declare a speaker", error.Message, StringComparison.OrdinalIgnoreCase);
      }

      [Fact]
      public void Parse_AcceptsExactTextDifferingOnlyByBoundaryWhitespace()
      {
        var response = MutateResponse(root => root["dialogue"]![0]!["endOffset"] = 20);
        var snapshot = CreateTrailingNewlineSnapshot();

        var cue = Assert.Single(new SceneBeatProductionParser().Parse("plan-1", response, snapshot).DialogueCues);

        Assert.Equal("You're still awake.\n", cue.ExactSourceText);
      }

      [Fact]
      public void Parse_AcceptsNormalizedTextSplittingHyphenatedCompound()
      {
        var response = MutateResponse(root =>
        {
          var cue = root["dialogue"]![0]!;
          cue["exactSourceText"] = "Pale-blue shirt.";
          cue["displayText"] = "Pale-blue shirt.";
          cue["normalizedSpokenText"] = "Pale blue shirt.";
          cue["startOffset"] = 0;
          cue["endOffset"] = 16;
        });
        var snapshot = CreateHyphenatedSnapshot();

        var cue = Assert.Single(new SceneBeatProductionParser().Parse("plan-1", response, snapshot).DialogueCues);

        Assert.Equal("Pale blue shirt.", cue.NormalizedSpokenText);
      }

      [Fact]
      public void Parse_RejectsNormalizedTextThatChangesWords()
      {
        var response = MutateResponse(root => root["dialogue"]![0]!["normalizedSpokenText"] = "You're still asleep.");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("changes semantic words", error.Message, StringComparison.OrdinalIgnoreCase);
      }

      [Fact]
      public void Parse_RejectsAuthoredSilenceWithSoundSources()
      {
        var response = MutateResponse(root => root["ambience"]!["authoredSilence"] = true);

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("cannot also declare sound sources", error.Message, StringComparison.OrdinalIgnoreCase);
      }

      [Fact]
      public void Parse_RejectsActionWithUnknownSubject()
      {
        var response = MutateResponse(root => root["actionArc"]![0]!["subjectKey"] = "p9");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("unknown Beat Production profile key", error.Message, StringComparison.OrdinalIgnoreCase);
      }

      [Fact]
      public void Parse_RejectsContinuityWithUnknownCharacter()
      {
        var response = MutateResponse(root => root["endContinuity"]!["characterStates"]![0]!["key"] = "p9");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("unknown Beat Production profile key", error.Message, StringComparison.OrdinalIgnoreCase);
      }

      [Theory]
      [InlineData("MomentHold", "start")]
      [InlineData("MomentAction", "start,end")]
      [InlineData("MomentTransition", "start,end")]
      [InlineData("BeatExcerpt", "start,end")]
      [InlineData("WholeBeat", "start,end")]
      public void Parse_AcceptsRequiredKeyStatesForEveryVideoCoverageKind(string kind, string roles)
      {
        var response = MutateResponse(root =>
        {
          var coverage = root["videoCoverage"]![0]!;
          coverage["kind"] = kind;
            coverage["requiredMomentRoles"] = new JsonArray(roles.Split(',').Select(role => JsonValue.Create(role)).ToArray());
        });

        var coverage = Assert.Single(Parse(response).VideoCoveragePlans);

        Assert.Equal(Enum.Parse<SceneVideoCoverageKind>(kind), coverage.CoverageKind);
        Assert.Equal(roles.Split(','), coverage.RequiredMomentRoles);
        Assert.Equal(2, coverage.AudioOwnership.Count);
      }

      [Fact]
      public void Parse_RejectsVideoCoverageMissingKindRequiredKeyState()
      {
        var response = MutateResponse(root =>
        {
          root["videoCoverage"]![0]!["kind"] = "WholeBeat";
          root["videoCoverage"]![0]!["requiredMomentRoles"] = new JsonArray("start");
        });

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("missing required key-state roles", error.Message, StringComparison.OrdinalIgnoreCase);
      }

    [Fact]
    public void Parse_RejectsCueOutsideBeatWindowWithoutContinuityTail()
    {
        var response = ValidResponse.Replace(
            "\"startSeconds\": 1, \"endSeconds\": 2, \"startEventKey\": \"e1\"",
            "\"startSeconds\": 1, \"endSeconds\": 5, \"startEventKey\": \"e1\"");

        var error = Assert.Throws<InvalidOperationException>(() => Parse(response));

        Assert.Contains("ends outside the Beat window", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneBeatProductionPlanData Parse(string response)
        => new SceneBeatProductionParser().Parse("plan-1", response, CreateSnapshot());

    private static string MutateResponse(Action<JsonObject> mutation)
    {
      var root = JsonNode.Parse(ValidResponse)!.AsObject();
      mutation(root);
      return root.ToJsonString();
    }

    internal static SceneBeatProductionSourceSnapshot CreateSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            new SceneBeatProductionBeatSnapshot(
                "b1", 1, "Conversation", "Dean speaks to Becky.", "entry hall",
                [new("Becky", "active", "p0"), new("Dean", "active", "p1")], ["n0", "c1"]),
            new SceneBeatProductionTurnSnapshot(
                "session-1", "turn-1", 1, "SubmitPrompt", DateTime.UtcNow, DateTime.UtcNow, new string('A', 64)),
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Dean speaks to Becky.", DateTime.UtcNow, new string('B', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "You're still awake.", DateTime.UtcNow, new string('C', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "Wife", "Female", "", "", "", false, new string('D', 64)),
                new("p1", "character-dean", "Dean", "Husband", "Male", "", "", "", true, new string('E', 64))
            ]);

    private static SceneBeatProductionSourceSnapshot CreateTrailingNewlineSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            new SceneBeatProductionBeatSnapshot(
                "b1", 1, "Conversation", "Dean speaks to Becky.", "entry hall",
                [new("Becky", "active", "p0"), new("Dean", "active", "p1")], ["n0", "c1"]),
            new SceneBeatProductionTurnSnapshot(
                "session-1", "turn-1", 1, "SubmitPrompt", DateTime.UtcNow, DateTime.UtcNow, new string('A', 64)),
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Dean speaks to Becky.", DateTime.UtcNow, new string('B', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "You're still awake.\n", DateTime.UtcNow, new string('C', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "Wife", "Female", "", "", "", false, new string('D', 64)),
                new("p1", "character-dean", "Dean", "Husband", "Male", "", "", "", true, new string('E', 64))
            ]);

    private static SceneBeatProductionSourceSnapshot CreateHyphenatedSnapshot()
        => new(
            1,
            "catalogue-1",
            1,
            new SceneBeatProductionBeatSnapshot(
                "b1", 1, "Conversation", "Dean speaks to Becky.", "entry hall",
                [new("Becky", "active", "p0"), new("Dean", "active", "p1")], ["n0", "c1"]),
            new SceneBeatProductionTurnSnapshot(
                "session-1", "turn-1", 1, "SubmitPrompt", DateTime.UtcNow, DateTime.UtcNow, new string('A', 64)),
            [
                new("n0", 0, "interaction-0", "Narrative", "System", "Dean speaks to Becky.", DateTime.UtcNow, new string('B', 64)),
                new("c1", 1, "interaction-1", "Dean", "User", "Pale-blue shirt.", DateTime.UtcNow, new string('C', 64))
            ],
            [
                new("p0", "character-becky", "Becky", "Wife", "Female", "", "", "", false, new string('D', 64)),
                new("p1", "character-dean", "Dean", "Husband", "Male", "", "", "", true, new string('E', 64))
            ]);
}