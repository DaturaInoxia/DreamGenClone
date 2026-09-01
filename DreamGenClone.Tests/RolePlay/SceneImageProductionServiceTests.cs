using DreamGenClone.Application.Abstractions;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageProductionServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetOrCreateGroup_ExactLineageAndPov_ReusesCurrentGroup()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = CreateGroupRequest();

        var created = await fixture.Service.GetOrCreateGroupAsync(request);
        var reused = await fixture.Service.GetOrCreateGroupAsync(request);

        Assert.Equal(created.Id, reused.Id);
        Assert.Equal("catalogue", reused.CatalogueId);
        Assert.Equal("plan", reused.BeatProductionPlanId);
        Assert.Equal(1, reused.BeatProductionPlanVersion);
        Assert.Equal("moment-set", reused.MomentSetId);
        Assert.Equal(1, reused.MomentSetVersion);
        Assert.Equal("moment", reused.MomentId);
        Assert.Equal("enrichment", reused.MomentEnrichmentId);
        Assert.Equal(1, reused.MomentEnrichmentRevision);
        Assert.Equal("Director", reused.Pov);
    }

    [Fact]
    public async Task GetOrCreateGroup_CurrentKeyWithDifferentLineage_FailsExplicitly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var request = CreateGroupRequest();
        _ = await fixture.Service.GetOrCreateGroupAsync(request);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetOrCreateGroupAsync(
            request with { SessionId = "different-session" }));

        Assert.Contains("immutable lineage", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DispositionAndQueries_UsePersistedCasAttemptsAndApprovalHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var group = await fixture.Service.GetOrCreateGroupAsync(CreateGroupRequest());
        var image = fixture.CreateImage("image-1", group.Id, SceneImageAttemptDisposition.Active, Now);
        await fixture.Images.InsertImageAsync(image);

        await fixture.Service.SetDispositionAsync(
            image.Id, group.Id, SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Shortlisted);
        var casError = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SetDispositionAsync(
            image.Id, group.Id, SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Rejected));
        Assert.Contains("changed concurrently", casError.Message, StringComparison.OrdinalIgnoreCase);

        var approval = await fixture.Service.ApproveAsync(
            group.Id, image.Id, image.Sha256!, "reviewer", "selected", CancellationToken.None);
        var attempts = await fixture.Service.ListAttemptsAsync(group.Id);
        var decisions = await fixture.Service.ListApprovalDecisionsAsync(group.Id);

        var persistedAttempt = Assert.Single(attempts);
        Assert.Equal(SceneImageAttemptDisposition.Shortlisted, persistedAttempt.Disposition);
        Assert.Equal(SceneImageStatus.Complete, persistedAttempt.Status);
        var persistedDecision = Assert.Single(decisions);
        Assert.Equal(approval.Id, persistedDecision.Id);
        Assert.Equal(image.Id, persistedDecision.SceneImageId);
        Assert.Equal(image.Sha256, persistedDecision.Sha256);
    }

    [Fact]
    public async Task PurgeRejectedBytes_MissingPolicy_FailsWithoutDeleting()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.InsertRejectedImageAsync("image-1", Now.AddDays(-10));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.PurgeRejectedBytesAsync("image-1", "operator"));

        Assert.Contains("not configured", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Storage.DeletedPaths);
    }

    [Fact]
    public async Task ManualPurge_UnprotectedRejectedImage_RemovesBytesAndPreservesMetadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SavePolicyAsync(SceneImageAttemptRetentionMode.Manual, null);
        await fixture.InsertRejectedImageAsync("image-1", Now);

        await fixture.Service.PurgeRejectedBytesAsync("image-1", "operator");

        Assert.Equal(["session/image-1.png"], fixture.Storage.DeletedPaths);
        var loaded = await fixture.Images.GetImageAsync("image-1");
        Assert.NotNull(loaded);
        Assert.Null(loaded!.FileRelativePath);
        Assert.Equal("SHA-image-1", loaded.Sha256);
        Assert.Equal("prompt-image-1", loaded.PromptSnapshot);
        Assert.Equal(SceneImageAttemptDisposition.Rejected, loaded.Disposition);
        Assert.Equal(Now, loaded.BytesPurgedUtc);
    }

    [Fact]
    public async Task AutomaticPurge_EnforcesPersistedRejectionAge()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SavePolicyAsync(SceneImageAttemptRetentionMode.Automatic, 30);
        await fixture.InsertRejectedImageAsync("too-new", Now.AddDays(-29));
        await fixture.InsertRejectedImageAsync("old-enough", Now.AddDays(-30));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.PurgeRejectedBytesAsync("too-new", "operator"));
        Assert.Contains("30-day", error.Message, StringComparison.Ordinal);

        await fixture.Service.PurgeRejectedBytesAsync("old-enough", "operator");
        Assert.Equal(["session/old-enough.png"], fixture.Storage.DeletedPaths);
    }

    [Theory]
    [InlineData("historical-approval", "approval decision history")]
    [InlineData("approval-ancestor", "ancestor of a current approval")]
    [InlineData("child", "descendant source-image")]
    [InlineData("edit-session", "edit-session source")]
    [InlineData("asset", "reusable scene asset")]
    [InlineData("identity", "identity reference asset")]
    public async Task PurgeRejectedBytes_BlocksPersistedReferences(string protection, string expectedMessage)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SavePolicyAsync(SceneImageAttemptRetentionMode.Manual, null);
        await fixture.InsertRejectedImageAsync("protected", Now.AddDays(-100));
        await fixture.AddProtectionAsync(protection, "protected");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.PurgeRejectedBytesAsync("protected", "operator"));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Storage.DeletedPaths);
        Assert.Equal("session/protected.png", (await fixture.Images.GetImageAsync("protected"))!.FileRelativePath);
    }

    [Fact]
    public async Task PromoteApprovedFrame_SharesExactFileAndProvenance_AndRejectsDuplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.InsertApprovedProductionAsync();

        var asset = await fixture.Service.PromoteApprovedFrameAsync(
            "group-1", "Hero location", SceneAssetType.Location, "{\"locationId\":\"loc-1\"}", null, "curator");

        Assert.Equal(SceneAssetKind.PromotedApprovedFrame, asset.Kind);
        Assert.Equal(SceneAssetStatus.Complete, asset.Status);
        Assert.Equal("session/approved.png", asset.FileRelativePath);
        Assert.Equal("SHA-approved", asset.SourceSha256);
        Assert.Equal("decision-1", asset.SourceApprovalDecisionId);
        Assert.Equal("approved", asset.SourceSceneImageId);
        Assert.Contains("curator", asset.SourceProvenanceJson, StringComparison.Ordinal);
        Assert.Equal(1, await fixture.Assets.CountByFilePathAsync("session/approved.png"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.PromoteApprovedFrameAsync(
            "group-1", "Hero location", SceneAssetType.Location, null, null, "curator"));
        Assert.Contains("already been promoted", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectedMoment_CompositionApprovalCleanupAndPromotion_PersistsEndToEnd()
    {
        await using var fixture = await Fixture.CreateAsync();
        var group = await fixture.Service.GetOrCreateGroupAsync(CreateGroupRequest());
        var approvedParent = fixture.CreateImage("composition-1", group.Id, SceneImageAttemptDisposition.Active, Now);
        var rejectedSibling = fixture.CreateImage("composition-2", group.Id, SceneImageAttemptDisposition.Active, Now);
        rejectedSibling.RegenerateOfId = approvedParent.Id;
        await fixture.Images.InsertImageAsync(approvedParent);
        await fixture.Images.InsertImageAsync(rejectedSibling);

        var initialAttempts = await fixture.Service.ListAttemptsAsync(group.Id);
        Assert.Equal(2, initialAttempts.Count);
        Assert.Contains(initialAttempts, attempt => attempt.Id == approvedParent.Id);
        var persistedSibling = Assert.Single(initialAttempts, attempt => attempt.Id == rejectedSibling.Id);
        Assert.Equal(approvedParent.Id, persistedSibling.RegenerateOfId);
        Assert.All(initialAttempts, attempt =>
        {
            Assert.Equal(SceneImageProductionStage.Composition, attempt.ProductionStage);
            Assert.Equal("[]", attempt.TypedReferenceSnapshotJson);
            Assert.Equal(group.MomentEnrichmentId, attempt.MomentEnrichmentId);
            Assert.Equal(group.MomentEnrichmentRevision, attempt.MomentEnrichmentRevision);
        });

        await fixture.Service.SetDispositionAsync(
            approvedParent.Id, group.Id, SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Shortlisted);
        await fixture.Service.SetDispositionAsync(
            rejectedSibling.Id, group.Id, SceneImageAttemptDisposition.Active, SceneImageAttemptDisposition.Rejected);
        var approval = await fixture.Service.ApproveAsync(
            group.Id, approvedParent.Id, approvedParent.Sha256!, "reviewer", "exact completed frame");
        Assert.Equal(ApprovedSceneFrameDecisionState.Approved, approval.Decision);
        Assert.Equal(approvedParent.Sha256, approval.Sha256);

        await fixture.SavePolicyAsync(SceneImageAttemptRetentionMode.Manual, null);
        await fixture.Service.PurgeRejectedBytesAsync(rejectedSibling.Id, "operator");

        var purged = await fixture.Images.GetImageAsync(rejectedSibling.Id);
        Assert.NotNull(purged);
        Assert.Null(purged!.FileRelativePath);
        Assert.Equal(rejectedSibling.PromptSnapshot, purged.PromptSnapshot);
        Assert.Equal(rejectedSibling.Sha256, purged.Sha256);
        Assert.Equal(Now, purged.BytesPurgedUtc);
        Assert.Equal(["session/composition-2.png"], fixture.Storage.DeletedPaths);
        var retainedApproved = await fixture.Images.GetImageAsync(approvedParent.Id);
        Assert.Equal("session/composition-1.png", retainedApproved!.FileRelativePath);
        Assert.Null(retainedApproved.BytesPurgedUtc);

        var asset = await fixture.Service.PromoteApprovedFrameAsync(
            group.Id, "Selected Moment frame", SceneAssetType.Location,
            "{\"momentId\":\"moment\"}", null, "curator");
        var persistedAsset = await fixture.Assets.GetAsync(asset.Id);
        Assert.NotNull(persistedAsset);
        Assert.Equal(retainedApproved.FileRelativePath, persistedAsset!.FileRelativePath);
        Assert.Equal(approval.Id, persistedAsset.SourceApprovalDecisionId);
        Assert.Equal(retainedApproved.Id, persistedAsset.SourceSceneImageId);
        Assert.Equal(retainedApproved.Sha256, persistedAsset.Sha256);
        Assert.Equal(retainedApproved.Sha256, persistedAsset.SourceSha256);
        Assert.Contains(group.Id, persistedAsset.SourceProvenanceJson, StringComparison.Ordinal);
        Assert.Contains(group.MomentEnrichmentId, persistedAsset.SourceProvenanceJson, StringComparison.Ordinal);
        Assert.Equal(1, await fixture.Assets.CountByFilePathAsync(retainedApproved.FileRelativePath!));
    }

    [Fact]
    public async Task RetentionPolicy_ValidatesRulesAndUsesVersionCas()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null(await fixture.Service.GetRetentionPolicyAsync());

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveRetentionPolicyAsync(
            new SceneImageAttemptRetentionPolicy { Mode = SceneImageAttemptRetentionMode.Manual, RejectedRetentionDays = 1, UpdatedBy = "operator", UpdatedUtc = Now }, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveRetentionPolicyAsync(
            new SceneImageAttemptRetentionPolicy { Mode = SceneImageAttemptRetentionMode.Automatic, UpdatedBy = "operator", UpdatedUtc = Now }, null));

        var saved = await fixture.SavePolicyAsync(SceneImageAttemptRetentionMode.Manual, null);
        Assert.Equal(1, saved.Version);
        var updated = await fixture.Groups.SaveRetentionPolicyAsync(new SceneImageAttemptRetentionPolicy
        {
            Mode = SceneImageAttemptRetentionMode.Automatic,
            RejectedRetentionDays = 45,
            UpdatedBy = "operator-2",
            UpdatedUtc = Now.AddMinutes(1)
        }, saved.Version);
        Assert.Equal(2, updated.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Groups.SaveRetentionPolicyAsync(updated, saved.Version));
    }

    private static CreateSceneImageProductionGroupRequest CreateGroupRequest()
        => new(
            "session",
            "interaction",
            "catalogue",
            "beat",
            "plan",
            1,
            "moment-set",
            1,
            "moment",
            "enrichment",
            1,
            "Director",
            null);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string databasePath, FixedTimeProvider timeProvider)
        {
            DatabasePath = databasePath;
            var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={databasePath};Pooling=False" });
            Groups = new SceneImageProductionGroupRepository(options);
            Images = new SceneImageRepository(options);
            Assets = new SceneAssetRepository(options);
            Storage = new RecordingStorage();
            Service = new SceneImageProductionService(
                Groups, Images, Assets, Storage, timeProvider, NullLogger<SceneImageProductionService>.Instance);
        }

        public string DatabasePath { get; }
        public SceneImageProductionGroupRepository Groups { get; }
        public SceneImageRepository Images { get; }
        public SceneAssetRepository Assets { get; }
        public RecordingStorage Storage { get; }
        public SceneImageProductionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(
                Path.Combine(Path.GetTempPath(), $"scene-image-production-service-{Guid.NewGuid():N}.db"),
                new FixedTimeProvider(Now));
            _ = await fixture.Groups.GetRetentionPolicyAsync();
            _ = await fixture.Images.GetImageAsync("schema-probe");
            _ = await fixture.Assets.GetAsync("schema-probe");
            await fixture.ExecuteAsync(
                "CREATE TABLE IF NOT EXISTS SceneMomentEnrichments (Id TEXT PRIMARY KEY, CatalogueId TEXT NOT NULL, BeatId TEXT NOT NULL, BeatProductionPlanId TEXT NOT NULL, BeatProductionPlanVersion INTEGER NOT NULL, MomentSetId TEXT NOT NULL, MomentSetVersion INTEGER NOT NULL, MomentId TEXT NOT NULL, Revision INTEGER NOT NULL, Status TEXT NOT NULL); INSERT OR IGNORE INTO SceneMomentEnrichments (Id, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion, MomentId, Revision, Status) VALUES ('enrichment', 'catalogue', 'beat', 'plan', 1, 'moment-set', 1, 'moment', 1, 'Complete');",
                "schema-probe");
            return fixture;
        }

        public Task<SceneImageAttemptRetentionPolicy> SavePolicyAsync(SceneImageAttemptRetentionMode mode, int? days)
            => Groups.SaveRetentionPolicyAsync(new SceneImageAttemptRetentionPolicy
            {
                Mode = mode,
                RejectedRetentionDays = days,
                UpdatedBy = "operator",
                UpdatedUtc = Now
            }, null);

        public Task InsertRejectedImageAsync(string id, DateTime rejectedUtc)
            => Images.InsertImageAsync(CreateImage(id, "group-1", SceneImageAttemptDisposition.Rejected, rejectedUtc));

        public async Task AddProtectionAsync(string protection, string imageId)
        {
            switch (protection)
            {
                case "historical-approval":
                    await InsertGroupAsync(null);
                    await ExecuteAsync("INSERT INTO ApprovedSceneFrameDecisions (Id, ProductionGroupId, Version, SceneImageId, Sha256, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Decision, DecidedBy, DecisionUtc) VALUES ('history', 'group-1', 1, $imageId, 'SHA-protected', 'c', 'b', 'p', 1, 'ms', 1, 'm', 'enrichment', 1, 'Superseded', 'reviewer', $now);", imageId);
                    break;
                case "approval-ancestor":
                    await Images.InsertImageAsync(CreateImage("approved-child", "group-1", SceneImageAttemptDisposition.Active, Now, imageId));
                    await InsertGroupAndDecisionAsync("approved-child");
                    break;
                case "child":
                    await Images.InsertImageAsync(CreateImage("child", "group-1", SceneImageAttemptDisposition.Active, Now, imageId));
                    break;
                case "edit-session":
                    await ExecuteAsync("CREATE TABLE IF NOT EXISTS SceneImageEditSessions (Id TEXT PRIMARY KEY, SourceImageId TEXT NOT NULL); INSERT INTO SceneImageEditSessions (Id, SourceImageId) VALUES ('edit-1', $imageId);", imageId);
                    break;
                case "asset":
                    await Assets.UpsertAsync(new SceneAsset { Id = "asset-1", Name = "Asset", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, FileRelativePath = $"session/{imageId}.png" });
                    break;
                case "identity":
                    await ExecuteAsync("CREATE TABLE IF NOT EXISTS SceneImageReferenceAssets (Id TEXT PRIMARY KEY, FileRelativePath TEXT NOT NULL); INSERT INTO SceneImageReferenceAssets (Id, FileRelativePath) VALUES ('reference-1', $path);", imageId);
                    break;
            }
        }

        public async Task InsertApprovedProductionAsync()
        {
            await Images.InsertImageAsync(CreateImage("approved", "group-1", SceneImageAttemptDisposition.Active, Now));
            await InsertGroupAndDecisionAsync("approved");
        }

        private async Task InsertGroupAndDecisionAsync(string approvedImageId)
        {
            await InsertGroupAsync("decision-1");
            await ExecuteAsync("INSERT OR REPLACE INTO ApprovedSceneFrameDecisions (Id, ProductionGroupId, Version, SceneImageId, Sha256, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Decision, DecidedBy, DecisionUtc) VALUES ('decision-1', 'group-1', 1, $imageId, $sha, 'catalogue', 'beat', 'plan', 1, 'moment-set', 1, 'moment', 'enrichment', 1, 'Approved', 'reviewer', $now);", approvedImageId);
        }

        private Task InsertGroupAsync(string? currentDecisionId)
            => ExecuteAsync($"INSERT OR REPLACE INTO SceneImageProductionGroups (Id, SessionId, InteractionId, CatalogueId, BeatId, BeatProductionPlanId, BeatProductionPlanVersion, MomentSetId, MomentSetVersion, MomentId, MomentEnrichmentId, MomentEnrichmentRevision, Pov, Status, IdentityPolicy, CurrentApprovedDecisionId, CreatedUtc, UpdatedUtc) VALUES ('group-1', 'session', 'interaction', 'catalogue', 'beat', 'plan', 1, 'moment-set', 1, 'moment', 'enrichment', 1, 'Director', '{(currentDecisionId is null ? "Review" : "Approved")}', 'Required', {(currentDecisionId is null ? "NULL" : $"'{currentDecisionId}'")}, $now, $now);", "group-1");

        private async Task ExecuteAsync(string sql, string imageId)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$imageId", imageId);
            command.Parameters.AddWithValue("$path", $"session/{imageId}.png");
            command.Parameters.AddWithValue("$sha", $"SHA-{imageId}");
            command.Parameters.AddWithValue("$now", Now.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }

        public SceneImageRecord CreateImage(
            string id,
            string groupId,
            SceneImageAttemptDisposition disposition,
            DateTime dispositionUpdatedUtc,
            string? sourceImageId = null)
            => new()
            {
                Id = id,
                SessionId = "session",
                InteractionId = "interaction",
                PromptRecordId = "prompt",
                PromptSnapshot = $"prompt-{id}",
                Status = SceneImageStatus.Complete,
                Operation = sourceImageId is null ? SceneImageOperation.Generate : SceneImageOperation.Edit,
                SourceImageId = sourceImageId,
                ProductionGroupId = groupId,
                ProductionStage = SceneImageProductionStage.Composition,
                Disposition = disposition,
                DispositionUpdatedUtc = dispositionUpdatedUtc,
                FileRelativePath = $"session/{id}.png",
                Sha256 = $"SHA-{id}",
                CatalogueId = "catalogue",
                BeatId = "beat",
                BeatProductionPlanId = "plan",
                BeatProductionPlanVersion = 1,
                MomentSetId = "moment-set",
                MomentSetVersion = 1,
                MomentId = "moment",
                MomentEnrichmentId = "enrichment",
                MomentEnrichmentRevision = 1,
                TypedReferenceSnapshotJson = "[]",
                CreatedUtc = Now,
                CompletedUtc = Now,
                UpdatedUtc = Now
            };

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try { File.Delete(DatabasePath + suffix); } catch { }
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingStorage : ISceneImageStorageService
    {
        public List<string> DeletedPaths { get; } = [];
        public Task<string> SaveAsync(string sessionId, string fileName, Stream content, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add(relativePath);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }
}
