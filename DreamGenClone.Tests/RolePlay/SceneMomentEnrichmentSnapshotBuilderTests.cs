using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentEnrichmentSnapshotBuilderTests
{
    private readonly SceneMomentEnrichmentSnapshotBuilder _builder = new();

    [Fact]
    public void Build_FreezesSelectedMomentParentsProfilesCuesAndOnlyCitedEvidencePlusNarrative()
    {
        var (moment, momentSet, plan) = SceneMomentEnrichmentTestFixture.CreateParents(includeUncitedEvidence: true);

        var snapshot = _builder.Build(moment, momentSet, plan);

        Assert.Equal("plan-1", snapshot.BeatProductionPlanId);
        Assert.Equal(2, snapshot.BeatProductionPlanVersion);
        Assert.Equal("moment-set-1", snapshot.Moment.MomentSetId);
        Assert.Equal(3, snapshot.Moment.MomentSetVersion);
        Assert.Equal(["p0", "p1"], snapshot.Moment.Participants.Select(item => item.ProfileKey));
        Assert.Equal(["character-becky", "character-dean"], snapshot.Moment.Participants.Select(item => item.CharacterId));
        Assert.Equal(["n0", "c1"], snapshot.Evidence.Select(item => item.Key));
        Assert.Equal("s1", Assert.Single(snapshot.SoundCues).CueKey);

        moment.Label = "mutated";
        momentSet.Version = 99;
        plan.ActionArcJson = "mutated";
        Assert.Equal("Exchanged look", snapshot.Moment.Label);
        Assert.Equal(3, snapshot.Moment.MomentSetVersion);
        Assert.Contains("turns toward", snapshot.ActionArcJson, StringComparison.Ordinal);

        var momentJson = _builder.SerializeMomentSnapshot(snapshot);
        var evidenceJson = _builder.SerializeEvidenceSnapshot(snapshot);
        Assert.DoesNotContain("interaction-0", momentJson, StringComparison.Ordinal);
        Assert.Contains("interaction-0", evidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction-9", evidenceJson, StringComparison.Ordinal);

        var rehydrated = _builder.Deserialize(momentJson, evidenceJson);
        Assert.Equal(snapshot.Moment.MomentId, rehydrated.Moment.MomentId);
        Assert.Equal(snapshot.Evidence.Select(item => item.Key), rehydrated.Evidence.Select(item => item.Key));
    }

    [Fact]
    public void Build_RejectsIncompleteOrMismatchedParentLineage()
    {
        var (moment, momentSet, plan) = SceneMomentEnrichmentTestFixture.CreateParents();
        momentSet.Status = SceneBeatCatalogueStatus.Processing;
        Assert.Throws<InvalidOperationException>(() => _builder.Build(moment, momentSet, plan));

        (moment, momentSet, plan) = SceneMomentEnrichmentTestFixture.CreateParents();
        momentSet.BeatProductionPlanVersion++;
        var error = Assert.Throws<InvalidOperationException>(() => _builder.Build(moment, momentSet, plan));
        Assert.Contains("lineage", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class SceneMomentEnrichmentTestFixture
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static SceneMomentEnrichmentSourceSnapshot CreateSnapshot()
    {
        var (moment, momentSet, plan) = CreateParents();
        return new SceneMomentEnrichmentSnapshotBuilder().Build(moment, momentSet, plan);
    }

    internal static (SceneMoment Moment, SceneMomentSet MomentSet, SceneBeatProductionPlan Plan) CreateParents(
        bool includeUncitedEvidence = false)
    {
        const string planId = "plan-1";
        var source = SceneBeatProductionParserTests.CreateSnapshot();
        var data = new SceneBeatProductionParser().Parse(planId, SceneBeatProductionParserTests.ValidResponse, source);
        var plan = new SceneBeatProductionPlan
        {
            Id = planId,
            CatalogueId = source.CatalogueId,
            BeatId = source.Beat.BeatId,
            CatalogueVersion = source.CatalogueVersion,
            Version = 2,
            Status = SceneBeatCatalogueStatus.Complete,
            SchemaVersion = source.SchemaVersion,
            PromptContractVersion = SceneBeatProductionContract.ContractVersion,
            SourceSnapshotJson = JsonSerializer.Serialize(source, JsonOptions),
            NarrativeArcJson = data.NarrativeArcJson,
            TimelineJson = data.TimelineJson,
            ActionArcJson = data.ActionArcJson,
            StartContinuityJson = data.StartContinuityJson,
            EndContinuityJson = data.EndContinuityJson,
            SoundEventCuesJson = data.SoundEventCuesJson,
            SoundCues = data.SoundCues,
            VideoCoveragePlans = data.VideoCoveragePlans
        };
        var discovery = new SceneMomentDiscoverySnapshotBuilder().Build(plan);
        if (includeUncitedEvidence)
        {
            discovery = discovery with
            {
                Evidence = discovery.Evidence.Append(new SceneMomentDiscoveryEvidenceSnapshot(
                    "c9", 9, "interaction-9", "Observer", "Character", "Uncited detail.", new string('9', 64))).ToArray()
            };
        }
        var moment = new SceneMoment
        {
            MomentSetId = "moment-set-1",
            MomentId = "m2",
            Order = 2,
            Label = "Exchanged look",
            TemporalAnchor = "the instant their gazes meet at event e1",
            FrozenState = "Becky stands inside the hall and meets Dean's raised gaze.",
            VisibleAction = "holding eye contact",
            ParticipantSummaryJson = "[{\"profileKey\":\"p0\",\"involvement\":\"active\"},{\"profileKey\":\"p1\",\"involvement\":\"observer\"}]",
            CompositionRationale = "The shared sightline creates a clear emotional center.",
            ProductionRolesJson = "[\"StillCandidate\",\"VideoEnd\",\"SoundEventAnchor\"]",
            EvidenceInteractionIdsJson = "[\"interaction-1\"]"
        };
        var builder = new SceneMomentDiscoverySnapshotBuilder();
        var momentSet = new SceneMomentSet
        {
            Id = "moment-set-1",
            CatalogueId = plan.CatalogueId,
            BeatId = plan.BeatId,
            BeatProductionPlanId = plan.Id,
            BeatProductionPlanVersion = plan.Version,
            Version = 3,
            Status = SceneBeatCatalogueStatus.Complete,
            SchemaVersion = discovery.SchemaVersion,
            PromptContractVersion = SceneMomentDiscoveryContract.ContractVersion,
            BeatSnapshotJson = builder.SerializeBeatSnapshot(discovery),
            TurnEvidenceSnapshotJson = builder.SerializeEvidenceSnapshot(discovery),
            Moments = [moment]
        };
        return (moment, momentSet, plan);
    }
}