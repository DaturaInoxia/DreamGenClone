using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageProductionGroupRepositoryTests
{
    [Fact]
    public async Task Create_CurrentCompletedEnrichment_PersistsExactLineageAndQueries()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "current");
            var group = CreateGroup(enrichment);

            await fixture.GroupRepository.CreateAsync(group);

            var loaded = await fixture.GroupRepository.GetAsync(group.Id);
            Assert.NotNull(loaded);
            Assert.Equal(enrichment.CatalogueId, loaded!.CatalogueId);
            Assert.Equal(enrichment.BeatId, loaded.BeatId);
            Assert.Equal(enrichment.BeatProductionPlanId, loaded.BeatProductionPlanId);
            Assert.Equal(enrichment.BeatProductionPlanVersion, loaded.BeatProductionPlanVersion);
            Assert.Equal(enrichment.MomentSetId, loaded.MomentSetId);
            Assert.Equal(enrichment.MomentSetVersion, loaded.MomentSetVersion);
            Assert.Equal(enrichment.MomentId, loaded.MomentId);
            Assert.Equal(enrichment.Id, loaded.MomentEnrichmentId);
            Assert.Equal(enrichment.Revision, loaded.MomentEnrichmentRevision);
            Assert.Equal(SceneImageIdentityPolicy.Required, loaded.IdentityPolicy);
            Assert.Null(loaded.IdentitySkipReason);

            var current = await fixture.GroupRepository.GetCurrentAsync(enrichment.Id, "director");
            Assert.Equal(group.Id, current!.Id);
            var byInteraction = await fixture.GroupRepository.ListByInteractionAsync("session-1", "interaction-1");
            Assert.Single(byInteraction);
            Assert.Equal(group.Id, byInteraction[0].Id);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Create_SupersededEnrichment_RejectsStaleLineage()
    {
        var fixture = CreateFixture();
        try
        {
            var stale = await CreateCompletedEnrichmentAsync(fixture, "stale");
            _ = await CreateCompletedEnrichmentAsync(fixture, "replacement");

            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.GroupRepository.CreateAsync(CreateGroup(stale)));

            Assert.Contains("complete, and be current", error.Message, StringComparison.Ordinal);
            Assert.Null(await fixture.GroupRepository.GetAsync("group-stale"));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Create_ValidatesIdentitySkipContract()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "identity");

            var requiredWithReason = CreateGroup(enrichment);
            requiredWithReason.IdentitySkipReason = "not allowed";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.GroupRepository.CreateAsync(requiredWithReason));

            var skippedWithoutReason = CreateGroup(enrichment);
            skippedWithoutReason.IdentityPolicy = SceneImageIdentityPolicy.SkippedByUser;
            skippedWithoutReason.IdentitySkipReason = "   ";
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.GroupRepository.CreateAsync(skippedWithoutReason));

            var skipped = CreateGroup(enrichment);
            skipped.IdentityPolicy = SceneImageIdentityPolicy.SkippedByUser;
            skipped.IdentitySkipReason = "Identity pack intentionally deferred";
            await fixture.GroupRepository.CreateAsync(skipped);

            var loaded = await fixture.GroupRepository.GetAsync(skipped.Id);
            Assert.Equal(SceneImageIdentityPolicy.SkippedByUser, loaded!.IdentityPolicy);
            Assert.Equal("Identity pack intentionally deferred", loaded.IdentitySkipReason);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task Approval_ReplacementAndRevoke_AreVersionedAndMaintainOneCurrentDecision()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "approval");
            var group = CreateGroup(enrichment);
            await fixture.GroupRepository.CreateAsync(group);
            var firstImage = await CreateApprovalImageAsync(fixture, group, "image-1", "sha-1", SceneImageAttemptDisposition.Active);
            var secondImage = await CreateApprovalImageAsync(fixture, group, "image-2", "sha-2", SceneImageAttemptDisposition.Shortlisted);

            var first = await fixture.GroupRepository.ApproveAsync(
                group.Id, firstImage.Id, firstImage.Sha256!, "reviewer-1", "first", DateTime.UtcNow);
            Assert.Equal(1, first.Version);
            Assert.Equal(ApprovedSceneFrameDecisionState.Approved, first.Decision);
            Assert.Equal(first.Id, (await fixture.GroupRepository.GetAsync(group.Id))!.CurrentApprovedDecisionId);

            var second = await fixture.GroupRepository.ApproveAsync(
                group.Id, secondImage.Id, secondImage.Sha256!, "reviewer-2", null, DateTime.UtcNow);
            Assert.Equal(2, second.Version);
            var replaced = await fixture.GroupRepository.ListApprovalDecisionsAsync(group.Id);
            Assert.Equal(2, replaced.Count);
            Assert.Equal(ApprovedSceneFrameDecisionState.Superseded, replaced[0].Decision);
            Assert.Equal(ApprovedSceneFrameDecisionState.Approved, replaced[1].Decision);
            Assert.Single(replaced, decision => decision.Decision == ApprovedSceneFrameDecisionState.Approved);
            Assert.Equal(second.Id, (await fixture.GroupRepository.GetApprovalDecisionAsync(second.Id))!.Id);

            var revoked = await fixture.GroupRepository.RevokeCurrentApprovalAsync(
                group.Id, "reviewer-3", "withdrawn", DateTime.UtcNow);
            Assert.Equal(3, revoked.Version);
            Assert.Equal(secondImage.Id, revoked.SceneImageId);
            Assert.Equal(secondImage.Sha256, revoked.Sha256);
            Assert.Equal(ApprovedSceneFrameDecisionState.Revoked, revoked.Decision);
            var decisions = await fixture.GroupRepository.ListApprovalDecisionsAsync(group.Id);
            Assert.Equal(
                [ApprovedSceneFrameDecisionState.Superseded, ApprovedSceneFrameDecisionState.Superseded, ApprovedSceneFrameDecisionState.Revoked],
                decisions.Select(decision => decision.Decision).ToArray());
            Assert.DoesNotContain(decisions, decision => decision.Decision == ApprovedSceneFrameDecisionState.Approved);
            var updatedGroup = await fixture.GroupRepository.GetAsync(group.Id);
            Assert.Null(updatedGroup!.CurrentApprovedDecisionId);
            Assert.Equal(SceneImageProductionGroupStatus.Review, updatedGroup.Status);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ReEnrichedMoment_KeepsEarlierTakesListedDispositionedAndApprovable()
    {
        var fixture = CreateFixture();
        try
        {
            var earlierEnrichment = await CreateCompletedEnrichmentAsync(fixture, "rev-2");
            var earlierGroup = CreateGroup(earlierEnrichment);
            await fixture.GroupRepository.CreateAsync(earlierGroup);
            var earlierImage = await CreateApprovalImageAsync(
                fixture, earlierGroup, "earlier-image", "sha-earlier", SceneImageAttemptDisposition.Active);

            // Re-enriching the same moment supersedes the earlier facts and creates a NEW group for the same
            // (moment set, moment, POV). The operator does nothing: the takes they already made are still takes of
            // that moment and must keep behaving like any other attempt.
            var currentEnrichment = await CreateCompletedEnrichmentAsync(fixture, "rev-3");
            var currentGroup = CreateGroup(currentEnrichment);
            await fixture.GroupRepository.CreateAsync(currentGroup);
            var currentImage = await CreateApprovalImageAsync(
                fixture, currentGroup, "current-image", "sha-current", SceneImageAttemptDisposition.Active);

            var listed = await fixture.ImageRepository.ListImagesByProductionGroupAsync(currentGroup);
            Assert.Equal(
                new[] { "current-image", "earlier-image" },
                listed.Select(image => image.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

            Assert.True(await fixture.ImageRepository.TrySetDispositionAsync(
                currentGroup,
                earlierImage.Id,
                SceneImageAttemptDisposition.Active,
                SceneImageAttemptDisposition.Shortlisted,
                DateTime.UtcNow));
            Assert.Equal(
                SceneImageAttemptDisposition.Shortlisted,
                (await fixture.ImageRepository.GetImageAsync(earlierImage.Id))!.Disposition);

            var decision = await fixture.GroupRepository.ApproveAsync(
                currentGroup.Id, earlierImage.Id, earlierImage.Sha256!, "reviewer", "carried over", DateTime.UtcNow);
            Assert.Equal(earlierImage.Id, decision.SceneImageId);
            Assert.Equal(earlierImage.Sha256, decision.Sha256);
            // The decision records the group that approved it; the image keeps the enrichment that actually rendered
            // it, so the recipe behind the approved bytes is never restated as something it was not.
            Assert.Equal(currentGroup.Id, decision.ProductionGroupId);
            Assert.Equal(earlierGroup.MomentEnrichmentId, (await fixture.ImageRepository.GetImageAsync(earlierImage.Id))!.MomentEnrichmentId);
            Assert.Equal(
                SceneImageAttemptDisposition.Active,
                (await fixture.ImageRepository.GetImageAsync(currentImage.Id))!.Disposition);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task ReEnrichedMoment_DoesNotAdoptTakesOfADifferentMomentOrPov()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "scoped");
            var group = CreateGroup(enrichment);
            await fixture.GroupRepository.CreateAsync(group);

            await fixture.ImageRepository.InsertImageAsync(CreateForeignImage(group, "other-moment", momentId: "moment-2"));
            await fixture.ImageRepository.InsertImageAsync(CreateForeignImage(group, "other-pov", pov: "Becky"));
            await fixture.ImageRepository.InsertImageAsync(CreateForeignImage(group, "other-set", momentSetId: "moment-set-2"));

            var listed = await fixture.ImageRepository.ListImagesByProductionGroupAsync(group);

            Assert.DoesNotContain(listed, image => image.Id == "other-moment");
            Assert.DoesNotContain(listed, image => image.Id == "other-pov");
            Assert.DoesNotContain(listed, image => image.Id == "other-set");
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    /// <summary>An attempt of a DIFFERENT composition: another group, and a different moment set, moment or POV.</summary>
    private static SceneImageRecord CreateForeignImage(
        SceneImageProductionGroup group,
        string imageId,
        string? momentSetId = null,
        string? momentId = null,
        string? pov = null)
        => new()
        {
            Id = imageId,
            SessionId = group.SessionId,
            InteractionId = group.InteractionId,
            PromptRecordId = "prompt-1",
            PromptSnapshot = "composition",
            Status = SceneImageStatus.Complete,
            ProductionGroupId = $"other-{group.Pov}",
            ProductionStage = SceneImageProductionStage.Composition,
            Disposition = SceneImageAttemptDisposition.Active,
            CatalogueId = group.CatalogueId,
            BeatId = group.BeatId,
            BeatProductionPlanId = group.BeatProductionPlanId,
            BeatProductionPlanVersion = group.BeatProductionPlanVersion,
            MomentSetId = momentSetId ?? group.MomentSetId,
            MomentSetVersion = group.MomentSetVersion,
            MomentId = momentId ?? group.MomentId,
            MomentEnrichmentId = "enrichment-elsewhere",
            MomentEnrichmentRevision = 1,
            Pov = pov ?? group.Pov,
            Sha256 = $"sha-{imageId}"
        };

    [Fact]
    public async Task Approve_RejectsChecksumMismatchAndRejectedAttempt()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "invalid-approval");
            var group = CreateGroup(enrichment);
            await fixture.GroupRepository.CreateAsync(group);
            var active = await CreateApprovalImageAsync(fixture, group, "active", "persisted-sha", SceneImageAttemptDisposition.Active);
            var checksum = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.GroupRepository.ApproveAsync(
                group.Id, active.Id, "different-sha", "reviewer", null, DateTime.UtcNow));
            Assert.Contains("exactly match", checksum.Message, StringComparison.OrdinalIgnoreCase);

            var rejected = await CreateApprovalImageAsync(fixture, group, "rejected", "rejected-sha", SceneImageAttemptDisposition.Rejected);
            var disposition = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.GroupRepository.ApproveAsync(
                group.Id, rejected.Id, rejected.Sha256!, "reviewer", null, DateTime.UtcNow));
            Assert.Contains("cannot be approved", disposition.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await fixture.GroupRepository.ListApprovalDecisionsAsync(group.Id));
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    [Fact]
    public async Task SetPromptDraft_RoundTripsAndClears()
    {
        var fixture = CreateFixture();
        try
        {
            var enrichment = await CreateCompletedEnrichmentAsync(fixture, "draft");
            var group = CreateGroup(enrichment);
            await fixture.GroupRepository.CreateAsync(group);
            Assert.Null((await fixture.GroupRepository.GetAsync(group.Id))!.PromptDraftJson);

            const string draftJson = """{"source":1,"userIntent":"She stands at the clothesline."}""";
            var updated = await fixture.GroupRepository.SetPromptDraftAsync(group.Id, draftJson, DateTime.UtcNow);
            Assert.Equal(draftJson, updated.PromptDraftJson);

            // Reopened from the store rather than read off the write result: the page reads the ROW.
            var reopened = await fixture.GroupRepository.GetAsync(group.Id);
            Assert.Equal(draftJson, reopened!.PromptDraftJson);

            var cleared = await fixture.GroupRepository.SetPromptDraftAsync(group.Id, null, DateTime.UtcNow);
            Assert.Null(cleared.PromptDraftJson);
            Assert.Null((await fixture.GroupRepository.GetAsync(group.Id))!.PromptDraftJson);
        }
        finally
        {
            Cleanup(fixture.DatabasePath);
        }
    }

    private static TestFixture CreateFixture()
    {
        var path = Path.Combine(Path.GetTempPath(), $"scene-image-production-group-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={path}" });
        return new TestFixture(
            new SceneMomentEnrichmentRepository(options),
            new SceneImageProductionGroupRepository(options),
            new SceneImageRepository(options),
            path);
    }

    private static async Task<SceneMomentEnrichment> CreateCompletedEnrichmentAsync(
        TestFixture fixture,
        string suffix)
    {
        var now = DateTime.UtcNow;
        var enrichment = new SceneMomentEnrichment
        {
            Id = $"enrichment-{suffix}",
            CatalogueId = "catalogue-1",
            BeatId = "beat-1",
            BeatProductionPlanId = "plan-1",
            BeatProductionPlanVersion = 3,
            MomentSetId = "moment-set-1",
            MomentSetVersion = 4,
            MomentId = "moment-1",
            SchemaVersion = 1,
            PromptContractVersion = "moment-enrichment-v1",
            MomentSnapshotJson = "{}",
            TurnEvidenceSnapshotJson = "{}",
            ExecutionSettingsJson = "{}",
            CreatedUtc = now,
            UpdatedUtc = now
        };
        var attempt = new SceneBeatAnalysisAttempt
        {
            Id = $"attempt-{suffix}",
            OwnerRecordId = enrichment.Id,
            AttemptNumber = 1,
            JobId = $"job-{suffix}",
            SystemPrompt = "system",
            UserPrompt = "user",
            ValidationDetailsJson = "{}",
            InputCharacters = 10,
            CreatedUtc = now,
            UpdatedUtc = now
        };
        enrichment.CurrentAttemptId = attempt.Id;

        await fixture.EnrichmentRepository.CreateRevisionAsync(enrichment, attempt);
        Assert.True(await fixture.EnrichmentRepository.TryStartAttemptAsync(
            enrichment.Id, attempt.Id, "model", "provider", DateTime.UtcNow));
        Assert.True(await fixture.EnrichmentRepository.TryCompleteAttemptAsync(
            enrichment.Id,
            attempt,
            new SceneMomentEnrichmentData("{}", "[]", "{}"),
            DateTime.UtcNow));
        return enrichment;
    }

    private static SceneImageProductionGroup CreateGroup(SceneMomentEnrichment enrichment)
    {
        var now = DateTime.UtcNow;
        return new SceneImageProductionGroup
        {
            Id = enrichment.Id.Replace("enrichment-", "group-", StringComparison.Ordinal),
            SessionId = "session-1",
            InteractionId = "interaction-1",
            CatalogueId = enrichment.CatalogueId,
            BeatId = enrichment.BeatId,
            BeatProductionPlanId = enrichment.BeatProductionPlanId,
            BeatProductionPlanVersion = enrichment.BeatProductionPlanVersion,
            MomentSetId = enrichment.MomentSetId,
            MomentSetVersion = enrichment.MomentSetVersion,
            MomentId = enrichment.MomentId,
            MomentEnrichmentId = enrichment.Id,
            MomentEnrichmentRevision = enrichment.Revision,
            Pov = "Director",
            CameraIntentSnapshotJson = "{\"framing\":\"wide\"}",
            Status = SceneImageProductionGroupStatus.Draft,
            IdentityPolicy = SceneImageIdentityPolicy.Required,
            CreatedUtc = now,
            UpdatedUtc = now
        };
    }

    private static async Task<SceneImageRecord> CreateApprovalImageAsync(
        TestFixture fixture,
        SceneImageProductionGroup group,
        string imageId,
        string sha256,
        SceneImageAttemptDisposition disposition)
    {
        var image = new SceneImageRecord
        {
            Id = imageId,
            SessionId = group.SessionId,
            InteractionId = group.InteractionId,
            PromptRecordId = "prompt-1",
            PromptSnapshot = "composition",
            Status = SceneImageStatus.Complete,
            ProductionGroupId = group.Id,
            ProductionStage = SceneImageProductionStage.Composition,
            Disposition = disposition,
            CatalogueId = group.CatalogueId,
            BeatId = group.BeatId,
            BeatProductionPlanId = group.BeatProductionPlanId,
            BeatProductionPlanVersion = group.BeatProductionPlanVersion,
            MomentSetId = group.MomentSetId,
            MomentSetVersion = group.MomentSetVersion,
            MomentId = group.MomentId,
            MomentEnrichmentId = group.MomentEnrichmentId,
            MomentEnrichmentRevision = group.MomentEnrichmentRevision,
            Pov = group.Pov,
            TypedReferenceSnapshotJson = "[]",
            Sha256 = sha256
        };
        await fixture.ImageRepository.InsertImageAsync(image);
        return image;
    }

    private static void Cleanup(string path)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try
            {
                if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
            catch
            {
            }
        }
    }

    private sealed record TestFixture(
        SceneMomentEnrichmentRepository EnrichmentRepository,
        SceneImageProductionGroupRepository GroupRepository,
        SceneImageRepository ImageRepository,
        string DatabasePath);
}
