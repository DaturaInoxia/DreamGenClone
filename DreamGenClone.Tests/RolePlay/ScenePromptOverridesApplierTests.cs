using System.Text.Json.Nodes;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ScenePromptOverridesApplierTests
{
    private const string Snapshot = """
    {
      "moment": {
        "visualDescription": "Moment visual description",
        "visibleAction": "Moment visible action",
        "participantSummary": [ { "name": "Dean" }, { "name": "Becky" } ]
      },
      "frozenState": {
        "location": "Original location",
        "environment": "Original environment",
        "timeOfDay": "Low afternoon",
        "lighting": "Original lighting",
        "mood": "Tense",
        "objects": [ "stone", "pines" ],
        "continuityState": "Becky arches; Dean kneels",
        "characters": [
          {
            "characterId": "dean", "name": "Dean", "profileKey": "p-dean",
            "physicalLocation": "Dean location", "position": "Dean position",
            "actionOrObservation": "Dean action", "sightline": "Dean sightline",
            "clothing": "Dean clothing", "visibleCharacterNames": [ "Dean", "Becky" ]
          },
          {
            "characterId": "becky", "name": "Becky", "profileKey": "p-becky",
            "physicalLocation": "Becky location", "position": "Becky position",
            "actionOrObservation": "Becky action", "sightline": "Becky sightline",
            "clothing": "Becky clothing", "visibleCharacterNames": [ "Dean", "Becky" ]
          }
        ]
      }
    }
    """;

    private const string ProfileKeyedSnapshot = """
    {
      "moment": {
        "frozenState": "Becky arches; Dean kneels",
        "visibleAction": "Moment visible action",
        "compositionRationale": "Tight low shot from Dean's side",
        "participantSummary": [ { "profileKey": "p-becky", "involvement": "active" }, { "profileKey": "p-dean", "involvement": "observer" } ]
      },
      "frozenState": {
        "visualDescription": "Full prose of the instant",
        "location": "Original location",
        "continuityState": "Becky arches; Dean kneels",
        "characters": [
          {
            "characterId": "becky", "name": "Becky", "profileKey": "p-becky", "clothing": "Blue swimsuit",
            "visibleCharacterNames": [ "Dean" ]
          },
          {
            "characterId": "dean", "name": "Dean", "profileKey": "p-dean", "clothing": "Open shirt",
            "visibleCharacterNames": [ "Becky" ]
          }
        ]
      },
      "continuity": {
        "start": {
          "characterStates": [ { "key": "p-becky", "value": "Lying on the stone" }, { "key": "p-dean", "value": "Kneeling beside her" } ],
          "wardrobeStates": [ { "key": "p-becky", "value": "swimsuit discarded" }, { "key": "p-dean", "value": "bare below the waist" } ],
          "stateSummary": "Opening summary"
        },
        "end": {
          "characterStates": [ { "key": "p-becky", "value": "Rigid, mid-climax" }, { "key": "p-dean", "value": "Watching" } ],
          "wardrobeStates": [ { "key": "p-becky", "value": "swimsuit discarded" }, { "key": "p-dean", "value": "bare below the waist" } ],
          "stateSummary": "Closing summary"
        }
      },
      "typedReferences": [
        { "referenceKey": "identity-p-becky", "subjectKey": "p-becky", "role": "CharacterIdentity" },
        { "referenceKey": "identity-p-dean", "subjectKey": "p-dean", "role": "CharacterIdentity" }
      ]
    }
    """;

    [Fact]
    public void ApplySnapshot_WithNoOverrides_ReturnsSnapshotUnchanged()
    {
        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, null);

        Assert.Equal(Snapshot, result.SnapshotJson);
        Assert.Empty(result.AppearanceOverrides);
        Assert.Empty(result.RemovedCharacters);
    }

    [Fact]
    public void ApplySnapshot_ReplacesScalarField_HardSubstitution()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "scene.location", Value = "Replaced location" }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides).SnapshotJson)!.AsObject();

        Assert.Equal("Replaced location", root["frozenState"]!["location"]!.GetValue<string>());
        Assert.DoesNotContain("Original location", ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides).SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_RemovesScalarField()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "scene.lighting", Removed = true }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides).SnapshotJson)!.AsObject();

        Assert.Null(root["frozenState"]!["lighting"]);
    }

    [Fact]
    public void ApplySnapshot_OverridesPerCharacterField()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "character:becky.clothing", Value = "naked" }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides).SnapshotJson)!.AsObject();
        var becky = root["frozenState"]!["characters"]!.AsArray()
            .First(node => node!["characterId"]!.GetValue<string>() == "becky");

        Assert.Equal("naked", becky!["clothing"]!.GetValue<string>());
    }

    [Fact]
    public void ApplySnapshot_RemoveCharacter_StripsCastParticipantsAndVisibleNames()
    {
        var overrides = new ScenePromptOverrides { RemovedCharacters = ["Dean"] };

        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides);
        var root = JsonNode.Parse(result.SnapshotJson)!.AsObject();
        var characters = root["frozenState"]!["characters"]!.AsArray();

        Assert.Single(characters);
        Assert.Equal("becky", characters[0]!["characterId"]!.GetValue<string>());
        Assert.DoesNotContain("Dean", characters[0]!["visibleCharacterNames"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.Single(root["moment"]!["participantSummary"]!.AsArray());
        Assert.Contains("Dean", result.RemovedCharacters);
    }

    [Fact]
    public void ApplySnapshot_RemoveCharacterAddressedById_StripsProfileKeyedStructures()
    {
        // The real payload keys the participant summary and the continuity state by profileKey (p0/p1)
        // while the composer addresses characters by id — a keyed-only match left every trace behind.
        var overrides = new ScenePromptOverrides { RemovedCharacters = ["becky"] };

        var result = ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, overrides);
        var root = JsonNode.Parse(result.SnapshotJson)!.AsObject();

        Assert.DoesNotContain(
            "becky",
            root["frozenState"]!["characters"]!.AsArray().Select(c => c!["characterId"]!.GetValue<string>()));
        var participants = root["moment"]!["participantSummary"]!.AsArray();
        Assert.DoesNotContain("p-becky", participants.Select(p => p!["profileKey"]!.GetValue<string>()));
        foreach (var block in (string[])["start", "end"])
        {
            var states = root["continuity"]![block]!["characterStates"]!.AsArray();
            Assert.DoesNotContain("p-becky", states.Select(s => s!["key"]!.GetValue<string>()));
            var wardrobe = root["continuity"]![block]!["wardrobeStates"]!.AsArray();
            Assert.DoesNotContain("p-becky", wardrobe.Select(s => s!["key"]!.GetValue<string>()));
        }

        Assert.DoesNotContain(
            "p-becky",
            root["typedReferences"]!.AsArray().Select(r => r!["subjectKey"]!.GetValue<string>()));
    }

    [Fact]
    public void ApplySnapshot_RemovesUnrecognizedElementKey_FailsFast()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "beat.something", Removed = true }]
        };

        Assert.Throws<InvalidOperationException>(
            () => ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides));
    }

    [Fact]
    public void ApplySnapshot_RemovesContinuityBlockElement()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "continuity.start.stateSummary", Removed = true }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, overrides).SnapshotJson)!.AsObject();

        Assert.Null(root["continuity"]!["start"]!["stateSummary"]);
        Assert.NotNull(root["continuity"]!["end"]!["stateSummary"]);
    }

    [Fact]
    public void ApplySnapshot_RemovesFrozenStateVisualDescriptionAndMomentRationale()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "frozenState.visualDescription", Removed = true },
                new ScenePromptFieldOverride { ElementKey = "moment.compositionRationale", Removed = true }
            ]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, overrides).SnapshotJson)!.AsObject();

        Assert.Null(root["frozenState"]!["visualDescription"]);
        Assert.Null(root["moment"]!["compositionRationale"]);
    }

    [Fact]
    public void ApplySnapshot_AppearanceOverrideAndRemoval_AreReported()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "character:dean.appearance", Value = "custom face text" },
                new ScenePromptFieldOverride { ElementKey = "character:becky.appearance", Removed = true }
            ]
        };

        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides);

        Assert.Equal("custom face text", result.AppearanceOverrides["dean"]);
        Assert.Equal(string.Empty, result.AppearanceOverrides["becky"]);
    }
}
