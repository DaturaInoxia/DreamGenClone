using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneMomentDiscoverySnapshotBuilderTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SceneMomentDiscoverySnapshotBuilder _builder = new();

    [Fact]
    public void Build_FreezesCompletedPlanCanonicalDataAndAuthoritativeEvidence()
    {
        var plan = CreatePlan();

        var snapshot = _builder.Build(plan);

        Assert.Equal("plan-1", snapshot.BeatProductionPlanId);
        Assert.Equal(2, snapshot.BeatProductionPlanVersion);
        Assert.Equal("b1", snapshot.BeatId);
        Assert.Equal(["interaction-0", "interaction-1"], snapshot.Evidence.Select(item => item.InteractionId));
        Assert.Equal(["p0", "p1"], snapshot.Profiles.Select(item => item.Key));
        var coverage = Assert.Single(snapshot.VideoCoverage);
        Assert.Equal("MomentTransition", coverage.CoverageKind);
        Assert.Equal(["start", "end"], coverage.RequiredMomentRoles);

        plan.NarrativeArcJson = "mutated";
        Assert.Contains("\"eventKey\": \"e1\"", snapshot.NarrativeArcJson, StringComparison.Ordinal);
        Assert.DoesNotContain("interaction-0", _builder.SerializeBeatSnapshot(snapshot), StringComparison.Ordinal);
        Assert.Contains("interaction-0", _builder.SerializeEvidenceSnapshot(snapshot), StringComparison.Ordinal);
    }

    [Fact]
    public void Build_RejectsIncompleteOrMismatchedPlanLineage()
    {
        var plan = CreatePlan();
        plan.Status = SceneBeatCatalogueStatus.Processing;
        Assert.Throws<InvalidOperationException>(() => _builder.Build(plan));

        plan = CreatePlan();
        plan.BeatId = "b2";
        var error = Assert.Throws<InvalidOperationException>(() => _builder.Build(plan));
        Assert.Contains("lineage does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static SceneBeatProductionPlan CreatePlan()
    {
        const string planId = "plan-1";
        var source = SceneBeatProductionParserTests.CreateSnapshot();
        var data = new SceneBeatProductionParser().Parse(
            planId,
            SceneBeatProductionParserTests.ValidResponse,
            source);
        return new SceneBeatProductionPlan
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
            VideoCoveragePlans = data.VideoCoveragePlans
        };
    }
}