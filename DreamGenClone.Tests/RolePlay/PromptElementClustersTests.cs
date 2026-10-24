using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The editor-side concept clusters, the snapshot de-duplicator, and the explicitness setting that turns a
/// "fix this in the prompt" into a deterministic change with visible coverage.
/// </summary>
public sealed class PromptElementClustersTests
{
    private const string SnapshotWithDuplicates = """
        {
          "moment": { "momentId": "m1", "frozenState": "Becky is bent over the workbench in the shaded shed.", "visibleAction": "Dean bends Becky over the workbench." },
          "frozenState": {
            "location": "Shed behind the trailers",
            "environment": "Shaded shed interior with grit in the air.",
            "lighting": "Bars of slat light; shaded shed interior.",
            "timeOfDay": "Morning",
            "mood": "Hushed",
            "continuityState": "Becky is bent over the workbench in the shaded shed."
          },
          "continuity": {
            "start": { "location": "Shed behind the trailers", "lighting": "Bars of slat light; shaded shed interior." },
            "end":   { "location": "Shed behind the trailers", "lighting": "Bars of slat light; shaded shed interior." }
          }
        }
        """;

    // ---------- clusters ----------

    [Fact]
    public void LightingCluster_EditsAllThreeLightingLeaves()
    {
        var cluster = PromptElementClusters.SceneCluster("lighting")!;

        Assert.Equal(
            ["frozenState.lighting", "continuity.start.lighting", "continuity.end.lighting"],
            cluster.EditScopes);
    }

    [Fact]
    public void LocationCluster_EditsThreeLeavesButRemovesTheWholeRoom()
    {
        var cluster = PromptElementClusters.SceneCluster("location")!;

        Assert.Equal(
            ["frozenState.location", "continuity.start.location", "continuity.end.location"],
            cluster.EditScopes);
        // "remove the room" must mean the SAME 18 elements a bound location reference removes.
        Assert.Equal(StepPromptElementPlan.LocationScopes, cluster.EffectiveRemoveScopes);
    }

    [Fact]
    public void CharacterCluster_ExpandsTheActorKey()
    {
        var cluster = PromptElementClusters.CharacterCluster("clothing")!;

        Assert.Equal(["character:p0.clothing"], PromptElementClusters.ScopesFor(cluster, "p0"));
    }

    [Fact]
    public void VisualDescriptionCluster_AddressesTheFrozenStateLeaf()
    {
        // The whole-event prose lives at frozenState.visualDescription, NOT moment.visualDescription — keying it wrong
        // once produced a box that was always empty and wrote a property nothing read (reported 2026-10-22).
        var cluster = PromptElementClusters.SceneCluster("visualDescription")!;

        Assert.Equal(["frozenState.visualDescription"], cluster.EditScopes);
        Assert.DoesNotContain(cluster.EditScopes, scope => scope.Contains("moment.visualDescription", StringComparison.Ordinal));
    }

    [Fact]
    public void ObjectsCluster_WritesTheArrayAwareKeyButReadsFromFrozenState()
    {
        var cluster = PromptElementClusters.SceneCluster("objects")!;

        Assert.Equal(["scene.objects"], cluster.EditScopes);
        Assert.Equal("frozenState.objects", cluster.ReadPath);
    }

    [Fact]
    public void MomentProseCluster_AddressesMomentFrozenState()
    {
        Assert.Equal(["moment.frozenState"], PromptElementClusters.SceneCluster("momentProse")!.EditScopes);
    }

    [Fact]
    public void AppearanceCluster_IsTheDeterministicSideChannel()
    {
        var cluster = PromptElementClusters.CharacterCluster("appearance")!;

        Assert.Equal(PromptElementConsumer.DeterministicAppearance, cluster.Consumer);
    }

    [Fact]
    public void PositionCluster_IsDeterministicallyRestated()
    {
        var cluster = PromptElementClusters.CharacterCluster("position")!;

        Assert.Equal(PromptElementConsumer.DeterministicFrame, cluster.Consumer);
    }

    // ---------- de-duplication ----------

    [Fact]
    public void DeDuplicate_CollapsesTheMomentContinuityCopy()
    {
        var result = SemanticSnapshotDeDuplicator.DeDuplicate(SnapshotWithDuplicates);

        var root = System.Text.Json.Nodes.JsonNode.Parse(result)!.AsObject();
        // The byte-identical global continuity-state copy is dropped; the moment's own prose (the owner) survives.
        Assert.Null(root["frozenState"]!["continuityState"]);
        Assert.Equal("Becky is bent over the workbench in the shaded shed.", root["moment"]!["frozenState"]!.GetValue<string>());
        // The frozen location/lighting survive once, and their byte-identical continuity copies are removed.
        var continuity = root["continuity"]!.AsObject();
        Assert.Null(continuity["start"]!["location"]);
        Assert.Null(continuity["start"]!["lighting"]);
        Assert.Null(continuity["end"]!["location"]);
        Assert.Null(continuity["end"]!["lighting"]);
        // The owners stay.
        Assert.Equal("Shed behind the trailers", root["frozenState"]!["location"]!.GetValue<string>());
        Assert.Equal("Bars of slat light; shaded shed interior.", root["frozenState"]!["lighting"]!.GetValue<string>());
        // The whole-event prose stays — it is not byte-identical to anything.
        Assert.Equal("Shaded shed interior with grit in the air.", root["frozenState"]!["environment"]!.GetValue<string>());
    }

    [Fact]
    public void DeDuplicate_KeepsADivergedCopy()
    {
        // When the operator made one copy differ, only still-identical copies collapse.
        var root = System.Text.Json.Nodes.JsonNode.Parse(SnapshotWithDuplicates)!.AsObject();
        root["continuity"]!["start"]!["lighting"] = "Bars of slat light; shaded shed interior."; // identical -> collapses
        root["continuity"]!["end"]!["lighting"] = "Full darkness."; // diverged -> survives

        var result = System.Text.Json.Nodes.JsonNode.Parse(
            SemanticSnapshotDeDuplicator.DeDuplicate(root.ToJsonString()))!.AsObject();

        Assert.Null(result["continuity"]!["start"]!["lighting"]);
        Assert.Equal("Full darkness.", result["continuity"]!["end"]!["lighting"]!.GetValue<string>());
        // The owner and its still-identical continuity copies behave as before.
        Assert.Equal("Bars of slat light; shaded shed interior.", result["frozenState"]!["lighting"]!.GetValue<string>());
        Assert.Null(result["continuity"]!["start"]!["location"]);
    }

    [Fact]
    public void DeDuplicate_UnreadableJsonReturnsInputUnchanged()
    {
        const string broken = "{ not json";
        Assert.Equal(broken, SemanticSnapshotDeDuplicator.DeDuplicate(broken));
    }

    // ---------- explicitness ----------

    [Fact]
    public void Explicitness_DefaultsToExplicit()
    {
        Assert.Equal(SceneImageExplicitness.Explicit, new SceneImageStudioSettings().Explicitness);
    }

    [Fact]
    public void Explicitness_EveryValueHasADirective()
    {
        foreach (var value in Enum.GetValues<SceneImageExplicitness>())
        {
            var directive = SceneImageExplicitnessWording.Directive(value);
            Assert.False(string.IsNullOrWhiteSpace(directive));
        }
    }

    [Fact]
    public void Explicitness_SfwAndExplicitDisagree()
    {
        Assert.NotEqual(
            SceneImageExplicitnessWording.Directive(SceneImageExplicitness.Sfw),
            SceneImageExplicitnessWording.Directive(SceneImageExplicitness.Explicit));
    }
}
