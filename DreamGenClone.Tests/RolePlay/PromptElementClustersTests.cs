using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.ImageStep;
using DreamGenClone.Web.Application.RolePlay.Models;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The editor-side concept clusters and the explicitness setting that turn a "fix this in the prompt" into a
/// deterministic change with visible coverage. B-167: the image payload carries one owner per fact, so the cluster
/// table and <see cref="StillBriefElements"/> are the two halves of the same contract.
/// </summary>
public sealed class PromptElementClustersTests
{
    private const string SnapshotWithDuplicates = """
        {
          "moment": { "momentId": "m1", "temporalAnchor": "t1", "compositionRationale": "wide" },
          "frozenState": {
            "location": "Shed behind the trailers",
            "environment": "Shaded shed interior with grit in the air.",
            "lighting": "Bars of slat light; shaded shed interior.",
            "timeOfDay": "Morning",
            "mood": "Hushed",
            "event": { "anchor": "workbench", "line": "Dean bends Becky over the workbench." },
            "contacts": [ { "from": "p1", "part": "hands", "to": "p0", "targetPart": "hips", "manner": "grip" } ],
            "characters": [ { "profileKey": "p0", "name": "Becky", "at": { "anchor": "workbench", "posture": "bent forward" }, "facing": { "toward": "the boards" } } ]
          }
        }
        """;

    // ---------- clusters ----------

    [Fact]
    public void LightingCluster_EditsTheSingleSelectedLeaf()
    {
        var cluster = PromptElementClusters.SceneCluster("lighting")!;

        Assert.Equal(["frozenState.lighting"], cluster.EditScopes);
    }

    [Fact]
    public void LocationCluster_EditsThePlaceAndRemovesTheRoomScopes()
    {
        var cluster = PromptElementClusters.SceneCluster("location")!;

        Assert.Equal(["frozenState.location"], cluster.EditScopes);
        Assert.Equal(StepPromptElementPlan.LocationScopes, cluster.EffectiveRemoveScopes);
        Assert.Contains("frozenState.location", cluster.EffectiveRemoveScopes);
    }

    [Fact]
    public void CharacterCluster_ExpandsTheActorKey()
    {
        var cluster = PromptElementClusters.CharacterCluster("clothing")!;

        Assert.Equal(["character:p0.clothing"], PromptElementClusters.ScopesFor(cluster, "p0"));
    }

    [Fact]
    public void WorldFactClusters_AddressTheV4Leaves()
    {
        Assert.Equal(["frozenState.event.line"], PromptElementClusters.SceneCluster("eventLine")!.EditScopes);
        Assert.Equal(["frozenState.contacts"], PromptElementClusters.SceneCluster("contacts")!.EditScopes);
        Assert.Equal(["character:p0.at.anchor"], PromptElementClusters.ScopesFor(PromptElementClusters.CharacterCluster("atAnchor")!, "p0"));
        Assert.Equal(["character:p0.at.posture"], PromptElementClusters.ScopesFor(PromptElementClusters.CharacterCluster("atPosture")!, "p0"));
        Assert.Equal(["character:p0.facing.toward"], PromptElementClusters.ScopesFor(PromptElementClusters.CharacterCluster("facing")!, "p0"));
    }

    [Fact]
    public void DroppedRestatements_HaveNoCluster()
    {
        // B-167 D2: the four duplicate prose rows are gone, and their element keys are not selectable at all.
        foreach (var key in (string[])["visualDescription", "momentProse", "visibleAction", "continuityState"])
        {
            Assert.Null(PromptElementClusters.SceneCluster(key));
        }

        foreach (var key in (string[])
        [
            "moment.frozenState", "moment.visibleAction", "frozenState.visualDescription",
            "frozenState.continuityState", "continuity.start.stateSummary"
        ])
        {
            Assert.Throws<InvalidOperationException>(() => StillBriefElements.RequireSelectable(key));
        }
    }

    [Fact]
    public void ObjectsCluster_WritesTheArrayAwareKeyButReadsFromFrozenState()
    {
        var cluster = PromptElementClusters.SceneCluster("objects")!;

        Assert.Equal(["scene.objects"], cluster.EditScopes);
        Assert.Equal("frozenState.objects", cluster.ReadPath);
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

    // ---------- element selection ----------

    [Fact]
    public void EveryClusterScope_IsSelectableInTheImageBrief()
    {
        foreach (var cluster in PromptElementClusters.Scene)
            foreach (var scope in cluster.EditScopes)
                StillBriefElements.RequireSelectable(scope);

        foreach (var cluster in PromptElementClusters.Character)
            foreach (var scope in PromptElementClusters.ScopesFor(cluster, "p0"))
                StillBriefElements.RequireSelectable(scope);
    }

    [Fact]
    public void ContactsEdit_IsWrittenBackInTheSameFormatItIsShown()
    {
        var payload = ScenePromptOverridesApplier.ApplySnapshot(
            SnapshotWithDuplicates,
            new ScenePromptOverrides
            {
                Fields =
                [
                    new ScenePromptFieldOverride
                    {
                        ElementKey = "frozenState.contacts",
                        Value = "p1|hands|p0|waist|press\np0|back|p1|chest|lean"
                    }
                ]
            });

        var root = System.Text.Json.Nodes.JsonNode.Parse(payload.SnapshotJson)!.AsObject();
        var contacts = root["frozenState"]!["contacts"]!.AsArray();
        Assert.Equal(2, contacts.Count);
        Assert.Equal("waist", contacts[0]!["targetPart"]!.GetValue<string>());
        Assert.Equal("press", contacts[0]!["manner"]!.GetValue<string>());
    }

    [Fact]
    public void WorldFactEdit_CreatesANestedLeafOnAV3Brief()
    {
        var payload = ScenePromptOverridesApplier.ApplySnapshot(
            SnapshotWithDuplicates,
            new ScenePromptOverrides
            {
                Fields =
                [
                    new ScenePromptFieldOverride { ElementKey = "character:p0.facing.toward", Value = "p1" }
                ]
            });

        var root = System.Text.Json.Nodes.JsonNode.Parse(payload.SnapshotJson)!.AsObject();
        Assert.Equal("p1", root["frozenState"]!["characters"]![0]!["facing"]!["toward"]!.GetValue<string>());
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
