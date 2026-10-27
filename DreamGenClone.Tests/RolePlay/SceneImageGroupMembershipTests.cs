using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The one rule that decides whether an attempt is part of a production group. Re-enriching a moment supersedes its
/// facts, not its composition, so takes made under an earlier enrichment must remain takes of the same group — while a
/// different moment set, moment, or point of view never joins one.
/// </summary>
public sealed class SceneImageGroupMembershipTests
{
    [Fact]
    public void Contains_TakeFromAnEarlierEnrichmentOfTheSameMoment_IsAMember()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var earlier = CreateImage(group, enrichmentId: "enrichment-rev-2", revision: 2, productionGroupId: "group-rev-2");

        Assert.True(SceneImageGroupMembership.Contains(group, earlier));
    }

    [Fact]
    public void Contains_OwnAttempt_IsAMember()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var own = CreateImage(group, group.MomentEnrichmentId, group.MomentEnrichmentRevision, group.Id);

        Assert.True(SceneImageGroupMembership.Contains(group, own));
    }

    [Fact]
    public void Contains_LegacyAttemptWithOwningGroupButNoLineage_IsAMember()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var legacy = new SceneImageRecord
        {
            SessionId = "session-1",
            InteractionId = "interaction-1",
            PromptRecordId = "prompt-1",
            PromptSnapshot = "legacy",
            Status = SceneImageStatus.Complete,
            ProductionGroupId = group.Id
        };

        Assert.True(SceneImageGroupMembership.Contains(group, legacy));
    }

    [Theory]
    [InlineData("other-moment-set", "moment-1", "Director")]
    [InlineData("moment-set-1", "moment-2", "Director")]
    [InlineData("moment-set-1", "moment-1", "Becky")]
    public void Contains_SameOrEarlierEnrichmentButADifferentComposition_IsNotAMember(
        string momentSetId,
        string momentId,
        string pov)
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var image = CreateImage(group, "enrichment-rev-2", 2, "group-rev-2");
        image.MomentSetId = momentSetId;
        image.MomentId = momentId;
        image.Pov = pov;

        Assert.False(SceneImageGroupMembership.Contains(group, image));
    }

    [Fact]
    public void Contains_GroupWithoutLineage_FallsBackToTheOwningGroupOnly()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        group.MomentSetId = string.Empty;
        group.MomentId = string.Empty;
        var other = CreateImage(group, "enrichment-rev-2", 2, "group-rev-2");

        Assert.False(SceneImageGroupMembership.Contains(group, other));
    }

    [Fact]
    public void ApprovalGate_RejectsAnAttemptOfADifferentMoment()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var stranger = CreateImage(group, "enrichment-other", 1, "group-other");
        stranger.MomentId = "moment-2";
        stranger.Sha256 = "sha";

        var blocker = SceneImageFrameApprovalGate.BlockerFor(group, stranger);

        Assert.NotNull(blocker);
        Assert.Equal(SceneImageFrameApprovalBlockerCode.NotInGroup, blocker!.Code);
    }

    [Fact]
    public void ApprovalGate_AcceptsACompleteTakeFromAnEarlierEnrichment()
    {
        var group = CreateGroup(enrichmentId: "enrichment-rev-3", revision: 3);
        var earlier = CreateImage(group, "enrichment-rev-2", 2, "group-rev-2");
        earlier.FileRelativePath = "images/earlier.png";
        earlier.Sha256 = "sha-earlier";

        Assert.Null(SceneImageFrameApprovalGate.BlockerFor(group, earlier));
    }

    private static SceneImageProductionGroup CreateGroup(string enrichmentId, int revision)
    {
        var now = DateTime.UtcNow;
        return new SceneImageProductionGroup
        {
            Id = $"group-{enrichmentId}",
            SessionId = "session-1",
            InteractionId = "interaction-1",
            CatalogueId = "catalogue-1",
            BeatId = "beat-1",
            BeatProductionPlanId = "plan-1",
            BeatProductionPlanVersion = 3,
            MomentSetId = "moment-set-1",
            MomentSetVersion = 4,
            MomentId = "moment-1",
            MomentEnrichmentId = enrichmentId,
            MomentEnrichmentRevision = revision,
            Pov = "Director",
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private static SceneImageRecord CreateImage(
        SceneImageProductionGroup group,
        string enrichmentId,
        int revision,
        string productionGroupId)
        => new()
        {
            SessionId = group.SessionId,
            InteractionId = group.InteractionId,
            PromptRecordId = "prompt-1",
            PromptSnapshot = "composition",
            Status = SceneImageStatus.Complete,
            ProductionGroupId = productionGroupId,
            ProductionStage = SceneImageProductionStage.Composition,
            Disposition = SceneImageAttemptDisposition.Active,
            CatalogueId = group.CatalogueId,
            BeatId = group.BeatId,
            BeatProductionPlanId = group.BeatProductionPlanId,
            BeatProductionPlanVersion = group.BeatProductionPlanVersion,
            MomentSetId = group.MomentSetId,
            MomentSetVersion = group.MomentSetVersion,
            MomentId = group.MomentId,
            MomentEnrichmentId = enrichmentId,
            MomentEnrichmentRevision = revision,
            Pov = group.Pov
        };
}
