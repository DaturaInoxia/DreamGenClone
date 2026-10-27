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

    /// <summary>
    /// The appearance suppression has to be readable by the component that consumes it, which matches a frozen
    /// character by <c>characterId</c> and then <c>name</c>.
    ///
    /// <para>
    /// The key a binding addresses a character by is the PARTICIPANT profile key (<c>p0</c>, <c>p1</c>, ...), so
    /// writing the raw key suppressed nothing at all — and did it silently. Measured live 2026-10-12: the prompt held
    /// an authoritative "do not describe the appearance for &lt;identity-owner id&gt;" notice AND the complete
    /// appearance description, so the model wrote the description and the bound face reference lost.
    /// </para>
    /// </summary>
    /// <summary>
    /// Only the OPERATOR's character VALUE edits are applied by
    /// <see cref="ScenePromptOverridesApplier.ApplyOperatorCharacterEdits"/>. The suppression half of the same override
    /// set — the room is removed because a location image supplies it — must not reach the projection, which derives the
    /// frame's geometry from the scene anchors.
    /// </summary>
    [Fact]
    public void ApplyOperatorCharacterEdits_AppliesOnlyCharacterValues()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "character:dean.actionOrObservation", Value = "Standing with pants undone" },
                new ScenePromptFieldOverride { ElementKey = "scene.location", Removed = true },
                new ScenePromptFieldOverride { ElementKey = "character:dean.clothing", Removed = true }
            ]
        };

        var edited = ScenePromptOverridesApplier.ApplyOperatorCharacterEdits(Snapshot, overrides);

        Assert.Contains("Standing with pants undone", edited, StringComparison.Ordinal);
        // The removals are the projection's own decision, never this application of the operator's text.
        Assert.Contains("Original location", edited, StringComparison.Ordinal);
        Assert.Contains("Dean clothing", edited, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyOperatorCharacterEdits_WithNoCharacterEdits_ReturnsTheSnapshotUnchanged()
        => Assert.Equal(
            Snapshot,
            ScenePromptOverridesApplier.ApplyOperatorCharacterEdits(
                Snapshot,
                new ScenePromptOverrides
                {
                    Fields = [new ScenePromptFieldOverride { ElementKey = "scene.mood", Removed = true }]
                }));

    [Fact]
    public void ApplySnapshot_AppearanceRemovalByProfileKey_KeysTheSuppressionByTheResolvedIdentity()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "character:p-becky.appearance", Removed = true }]
        };

        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides);

        Assert.Equal(string.Empty, result.AppearanceOverrides["becky"]);
    }

    [Fact]
    public void ApplySnapshot_AppearanceElementRemoval_KeysOnlyThatElement()
    {
        // B-166 D10: a face reference removes the FACE element and leaves the BUILD element alone.
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "character:p-becky.appearance.face", Removed = true }
            ]
        };

        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides);

        Assert.Equal(string.Empty, result.AppearanceElementOverrides!["becky|face"]);
        Assert.False(result.AppearanceElementOverrides!.ContainsKey("becky|build"));
        Assert.Empty(result.AppearanceOverrides);
    }

    [Fact]
    public void ApplySnapshot_BodyBinding_RemovesFaceAndBuildElements()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields =
            [
                new ScenePromptFieldOverride { ElementKey = "character:p-becky.appearance.face", Removed = true },
                new ScenePromptFieldOverride { ElementKey = "character:p-becky.appearance.build", Removed = true }
            ]
        };

        var result = ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides);

        Assert.Equal(string.Empty, result.AppearanceElementOverrides!["becky|face"]);
        Assert.Equal(string.Empty, result.AppearanceElementOverrides!["becky|build"]);
    }

    /// <summary>
    /// A removal that resolves to no character in the brief is a contract-drift bug, and it used to be invisible:
    /// the applier wrote the key through and the notice printed the raw GUID. It fails fast now and names the
    /// characters the brief does contain, because a suppression nobody can read is a prompt that describes an element
    /// a reference image already supplies.
    /// </summary>
    [Fact]
    public void ApplySnapshot_AppearanceRemovalForACharacterTheBriefDoesNotContain_FailsFast()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "character:de351eb3-1111.appearance", Removed = true }]
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides));

        Assert.Contains("de351eb3-1111", error.Message, StringComparison.Ordinal);
        Assert.Contains("p-becky", error.Message, StringComparison.Ordinal);
        Assert.Contains("p-dean", error.Message, StringComparison.Ordinal);
    }

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

    /// <summary>
    /// The exemption a bound LOCATION needs: the room's lighting is in the image, so the prose must go with it —
    /// while time of day (a narrative anchor) and mood (not a claim about what the image owns) stay. This is the
    /// element map's <c>frozenState.lighting</c> removal proven against a real snapshot, not just against the map.
    /// </summary>
    [Fact]
    public void ApplySnapshot_RemovesTheLightingButKeepsTimeOfDayAndMood()
    {
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.lighting", Removed = true }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(Snapshot, overrides).SnapshotJson)!.AsObject();

        Assert.Null(root["frozenState"]!["lighting"]);
        Assert.NotNull(root["frozenState"]!["timeOfDay"]);
        Assert.NotNull(root["frozenState"]!["mood"]);
    }

    [Fact]
    public void ApplySnapshot_RefusesADroppedRestatementByName()
    {
        // B-167 D2: `continuity.*` is not part of the image brief at all. Addressing it must fail loudly and say why,
        // never no-op and never re-create the block.
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "continuity.start.stateSummary", Removed = true }]
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, overrides));

        Assert.Contains("continuity.start.stateSummary", error.Message, StringComparison.Ordinal);
        Assert.Contains("not part of the compiled Still brief", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplySnapshot_RemovesTheEventLineAndKeepsTheAnchor()
    {
        const string withEvent = """
            {"moment":{},"frozenState":{"event":{"anchor":"workbench","line":"Dean bends Becky forward."}}}
            """;
        var overrides = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.event.line", Removed = true }]
        };

        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(withEvent, overrides).SnapshotJson)!.AsObject();

        Assert.Null(root["frozenState"]!["event"]!["line"]);
        Assert.Equal("workbench", root["frozenState"]!["event"]!["anchor"]!.GetValue<string>());
    }

    [Fact]
    public void ApplySnapshot_RefusesVisualDescriptionAndKeepsTheCompositionRationale()
    {
        // The visual description is a dropped restatement; the composition rationale IS selected, and the two used to
        // be edited together. The refusal names the dropped key, so the operator is told where the fact now lives.
        var dropped = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "frozenState.visualDescription", Removed = true }]
        };
        var error = Assert.Throws<InvalidOperationException>(
            () => ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, dropped));
        Assert.Contains("frozenState.visualDescription", error.Message, StringComparison.Ordinal);
        Assert.Contains("frozenState.event.line", error.Message, StringComparison.Ordinal);

        var rationale = new ScenePromptOverrides
        {
            Fields = [new ScenePromptFieldOverride { ElementKey = "moment.compositionRationale", Removed = true }]
        };
        var root = JsonNode.Parse(ScenePromptOverridesApplier.ApplySnapshot(ProfileKeyedSnapshot, rationale).SnapshotJson)!.AsObject();
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
