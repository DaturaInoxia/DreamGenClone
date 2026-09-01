using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageRepositoryTests
{
    private static (SceneImageRepository repo, string dbPath) CreateRepo()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"scene-image-repo-{Guid.NewGuid():N}.db");
        var repo = new SceneImageRepository(
            Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath}" }));
        return (repo, dbPath);
    }

    [Fact]
    public async Task BeatAnalysis_UpsertByTurn_ReplacesCurrentAnalysis()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var first = new SceneImageBeatAnalysisRecord
            {
                SessionId = "s1",
                TurnId = "t1",
                AnchorInteractionId = "i1",
                Status = SceneImageBeatAnalysisStatus.Pending,
                InputSnapshotJson = "{\"turn\":1}"
            };
            await repo.UpsertBeatAnalysisAsync(first);

            var loaded = await repo.GetBeatAnalysisByTurnAsync("s1", "t1");
            Assert.NotNull(loaded);
            Assert.Equal(first.Id, loaded!.Id);
            Assert.Equal(SceneImageBeatAnalysisStatus.Pending, loaded.Status);

            var replacement = new SceneImageBeatAnalysisRecord
            {
                SessionId = "s1",
                TurnId = "t1",
                AnchorInteractionId = "i2",
                Status = SceneImageBeatAnalysisStatus.Complete,
                BeatsJson = "[{\"beatId\":\"b1\"}]",
                InputSnapshotJson = "{\"turn\":1}",
                RawModelResponse = "{\"beats\":[]}",
                ModelIdentifier = "model-1"
            };
            await repo.UpsertBeatAnalysisAsync(replacement);

            loaded = await repo.GetBeatAnalysisByTurnAsync("s1", "t1");
            Assert.NotNull(loaded);
            Assert.Equal(replacement.Id, loaded!.Id);
            Assert.Equal("i2", loaded.AnchorInteractionId);
            Assert.Equal(SceneImageBeatAnalysisStatus.Complete, loaded.Status);
            Assert.Equal("model-1", loaded.ModelIdentifier);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpsertPrompt_Get_GetLatest_Works()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var prompt = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-1",
                BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
                Pov = "Omniscient",
                SettingsJson = "{\"Style\":\"anime\"}",
                InputExcerpt = "an excerpt",
                Status = SceneImagePromptStatus.Pending
            };
            await repo.UpsertPromptAsync(prompt);

            var loaded = await repo.GetPromptAsync(prompt.Id);
            Assert.NotNull(loaded);
            Assert.Equal(prompt.Id, loaded!.Id);
            Assert.Equal(SceneImagePromptStatus.Pending, loaded.Status);
            Assert.Equal("anime", System.Text.Json.JsonDocument.Parse(loaded.SettingsJson).RootElement.GetProperty("Style").GetString());

            // Transition Pending -> Complete via upsert on the same record.
            prompt.Status = SceneImagePromptStatus.Complete;
            prompt.OutputPrompt = "a dramatic anime scene";
            prompt.ModelIdentifier = "deepseek";
            prompt.UpdatedUtc = DateTime.UtcNow;
            await repo.UpsertPromptAsync(prompt);

            var latest = await repo.GetLatestPromptAsync("s1", "i1");
            Assert.NotNull(latest);
            Assert.Equal(SceneImagePromptStatus.Complete, latest!.Status);
            Assert.Equal("a dramatic anime scene", latest.OutputPrompt);
            Assert.Equal("deepseek", latest.ModelIdentifier);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpsertPrompt_CanonicalLineage_RoundTripsAndQueriesExactGroupAndBrief()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var prompt = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = string.Empty,
                BeatSnapshotJson = string.Empty,
                ProductionGroupId = "group-1",
                CompiledMediaBriefId = "brief-1",
                Pov = "Omniscient",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "canonical prompt"
            };

            await repo.UpsertPromptAsync(prompt);

            var loaded = await repo.GetLatestCompletedProductionPromptAsync("s1", "i1", "group-1", "brief-1");
            Assert.NotNull(loaded);
            Assert.Equal(prompt.Id, loaded!.Id);
            Assert.Equal("group-1", loaded.ProductionGroupId);
            Assert.Equal("brief-1", loaded.CompiledMediaBriefId);
            Assert.Equal(string.Empty, loaded.BeatAnalysisId);
            Assert.Equal(string.Empty, loaded.BeatSnapshotJson);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Theory]
    [InlineData("", "", null, null)]
    [InlineData("analysis-1", "{\"beatId\":\"beat-1\"}", "group-1", "brief-1")]
    [InlineData("analysis-1", "", null, null)]
    [InlineData("", "", "group-1", null)]
    public async Task UpsertPrompt_InvalidLineageMode_Fails(
        string beatAnalysisId,
        string beatSnapshotJson,
        string? productionGroupId,
        string? compiledMediaBriefId)
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var prompt = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = beatAnalysisId,
                BeatSnapshotJson = beatSnapshotJson,
                ProductionGroupId = productionGroupId,
                CompiledMediaBriefId = compiledMediaBriefId,
                Pov = "Omniscient"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.UpsertPromptAsync(prompt));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetLatestPrompt_ReturnsNewestFirst()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var older = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-1",
                BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
                Pov = "Omniscient",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "first",
                UpdatedUtc = DateTime.UtcNow.AddMinutes(-5)
            };
            var newer = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-1",
                BeatSnapshotJson = "{\"beatId\":\"beat-2\"}",
                Pov = "Omniscient",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "second",
                UpdatedUtc = DateTime.UtcNow
            };
            await repo.UpsertPromptAsync(older);
            await repo.UpsertPromptAsync(newer);

            var latest = await repo.GetLatestPromptAsync("s1", "i1");
            Assert.NotNull(latest);
            Assert.Equal(newer.Id, latest!.Id);
            Assert.Equal("second", latest.OutputPrompt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task GetLatestCompletedPrompt_MatchesCurrentAnalysisBeatAndPov()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var matching = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-2",
                BeatSnapshotJson = "{\"beatId\":\"beat-3\"}",
                Pov = "Dean",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "saved Dean prompt",
                UpdatedUtc = DateTime.UtcNow.AddMinutes(-1)
            };
            var newerPending = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-2",
                BeatSnapshotJson = "{\"beatId\":\"beat-3\"}",
                Pov = "Dean",
                Status = SceneImagePromptStatus.Pending,
                UpdatedUtc = DateTime.UtcNow
            };
            var otherPov = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-2",
                BeatSnapshotJson = "{\"beatId\":\"beat-3\"}",
                Pov = "Omniscient",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "other POV",
                UpdatedUtc = DateTime.UtcNow
            };
            await repo.UpsertPromptAsync(matching);
            await repo.UpsertPromptAsync(newerPending);
            await repo.UpsertPromptAsync(otherPov);

            var loaded = await repo.GetLatestCompletedPromptAsync(
                "s1", "i1", "analysis-2", "beat-3", "dean");

            Assert.NotNull(loaded);
            Assert.Equal(matching.Id, loaded!.Id);
            Assert.Equal("saved Dean prompt", loaded.OutputPrompt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InsertImage_StatusTransitions_Count_List_Delete()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var image = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "p1",
                PromptSnapshot = "a cat in a hat",
                Status = SceneImageStatus.Pending,
                SettingsJson = "{\"Style\":\"cartoon\",\"ImageSize\":\"1024x1024\",\"AllowExplicitImage\":true}",
                Style = "cartoon"
            };
            await repo.InsertImageAsync(image);

            // Pending -> Generating -> Complete
            image.Status = SceneImageStatus.Generating;
            image.StartedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await repo.InsertImageAsync(image);

            image.Status = SceneImageStatus.Complete;
            image.FileRelativePath = "s1/img.png";
            image.ModelIdentifier = "flux";
            image.ProviderName = "Together";
            image.CompletedUtc = DateTime.UtcNow;
            image.UpdatedUtc = DateTime.UtcNow;
            await repo.InsertImageAsync(image);

            var loaded = await repo.GetImageAsync(image.Id);
            Assert.NotNull(loaded);
            Assert.Equal(SceneImageStatus.Complete, loaded!.Status);
            Assert.Equal("s1/img.png", loaded.FileRelativePath);
            Assert.Equal("Together", loaded.ProviderName);
            // CR-003: full settings snapshot persisted with the image.
            Assert.Equal("cartoon", loaded.Style);
            Assert.Contains("AllowExplicitImage", loaded.SettingsJson, StringComparison.Ordinal);

            var byInteraction = await repo.ListImagesByInteractionAsync("s1", "i1");
            Assert.Single(byInteraction);

            var bySession = await repo.ListImagesBySessionAsync("s1");
            Assert.Single(bySession);

            var counts = await repo.CountImagesByInteractionAsync("s1");
            Assert.True(counts.ContainsKey("i1"));
            Assert.Equal(1, counts["i1"]);

            // Failed images must not count toward the Complete-only indicator count.
            var failed = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i2",
                PromptRecordId = "p2",
                PromptSnapshot = "x",
                Status = SceneImageStatus.Failed,
                ErrorMessage = "boom"
            };
            await repo.InsertImageAsync(failed);
            counts = await repo.CountImagesByInteractionAsync("s1");
            Assert.Equal(1, counts["i1"]);
            Assert.False(counts.ContainsKey("i2"));

            await repo.DeleteImageAsync(image.Id);
            Assert.Null(await repo.GetImageAsync(image.Id));
            Assert.Single(await repo.ListImagesBySessionAsync("s1")); // failed remains
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InsertImage_PersistsRenderModeAndIdentityPackId()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var image = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "p1",
                PromptSnapshot = "a single man",
                Status = SceneImageStatus.Pending,
                RenderMode = SceneImageRenderMode.IdentityControlled,
                IdentityPackId = "pack-1"
            };
            await repo.InsertImageAsync(image);

            var loaded = await repo.GetImageAsync(image.Id);
            Assert.NotNull(loaded);
            Assert.Equal(SceneImageRenderMode.IdentityControlled, loaded!.RenderMode);
            Assert.Equal("pack-1", loaded.IdentityPackId);

            // Prompt-only default round-trips with a null pack id.
            var promptOnly = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i2",
                PromptRecordId = "p2",
                PromptSnapshot = "just a prompt",
                Status = SceneImageStatus.Pending
            };
            await repo.InsertImageAsync(promptOnly);
            var loadedPromptOnly = await repo.GetImageAsync(promptOnly.Id);
            Assert.Equal(SceneImageRenderMode.PromptOnly, loadedPromptOnly!.RenderMode);
            Assert.Null(loadedPromptOnly.IdentityPackId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InsertImage_PersistsMultiCharacterIdentityPacksJson()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var packsJson = "[{\"packId\":\"pack-1\",\"characterLabel\":\"Dean — v1\",\"strength\":0.8}," +
                            "{\"packId\":\"pack-2\",\"characterLabel\":\"Becky — v1\"}]";
            var image = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "p1",
                PromptSnapshot = "two people",
                Status = SceneImageStatus.Pending,
                RenderMode = SceneImageRenderMode.IdentityControlled,
                IdentityPackId = "pack-1",
                IdentityPacksJson = packsJson
            };
            await repo.InsertImageAsync(image);

            var loaded = await repo.GetImageAsync(image.Id);
            Assert.NotNull(loaded);
            Assert.Equal("pack-1", loaded!.IdentityPackId);
            Assert.Equal(packsJson, loaded.IdentityPacksJson);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InsertImage_ProductionLineage_RoundTripsAndListsByGroup()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var purgedUtc = DateTime.UtcNow;
            var image = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "p1",
                PromptSnapshot = "position-focused composition",
                Status = SceneImageStatus.Complete,
                Operation = SceneImageOperation.Edit,
                SourceImageId = "parent-image-1",
                ProductionGroupId = "production-group-1",
                ProductionStage = SceneImageProductionStage.Finish,
                Disposition = SceneImageAttemptDisposition.Shortlisted,
                CatalogueId = "catalogue-1",
                BeatProductionPlanId = "plan-1",
                BeatProductionPlanVersion = 3,
                MomentSetId = "moment-set-1",
                MomentSetVersion = 4,
                MomentId = "moment-1",
                MomentEnrichmentId = "enrichment-1",
                MomentEnrichmentRevision = 2,
                TypedReferenceSnapshotJson = "[{\"role\":\"CharacterFace\",\"id\":\"pack-1\"}]",
                Sha256 = "abc123",
                BytesPurgedUtc = purgedUtc
            };
            await repo.InsertImageAsync(image);
            await repo.InsertImageAsync(new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "legacy-prompt",
                PromptSnapshot = "legacy image",
                Status = SceneImageStatus.Complete
            });

            var loaded = await repo.GetImageAsync(image.Id);
            Assert.NotNull(loaded);
            Assert.Equal("parent-image-1", loaded!.SourceImageId);
            Assert.Equal("production-group-1", loaded.ProductionGroupId);
            Assert.Equal(SceneImageProductionStage.Finish, loaded.ProductionStage);
            Assert.Equal(SceneImageAttemptDisposition.Shortlisted, loaded.Disposition);
            Assert.Equal("catalogue-1", loaded.CatalogueId);
            Assert.Equal("plan-1", loaded.BeatProductionPlanId);
            Assert.Equal(3, loaded.BeatProductionPlanVersion);
            Assert.Equal("moment-set-1", loaded.MomentSetId);
            Assert.Equal(4, loaded.MomentSetVersion);
            Assert.Equal("moment-1", loaded.MomentId);
            Assert.Equal("enrichment-1", loaded.MomentEnrichmentId);
            Assert.Equal(2, loaded.MomentEnrichmentRevision);
            Assert.Equal(image.TypedReferenceSnapshotJson, loaded.TypedReferenceSnapshotJson);
            Assert.Equal("abc123", loaded.Sha256);
            Assert.Equal(purgedUtc, loaded.BytesPurgedUtc);

            var byGroup = await repo.ListImagesByProductionGroupAsync("production-group-1");
            Assert.Single(byGroup);
            Assert.Equal(image.Id, byGroup[0].Id);

            var allImages = await repo.ListImagesByInteractionAsync("s1", "i1");
            var legacy = Assert.Single(allImages, candidate => candidate.PromptRecordId == "legacy-prompt");
            Assert.Null(legacy.ProductionGroupId);
            Assert.Null(legacy.ProductionStage);
            Assert.Null(legacy.Disposition);
            Assert.Null(legacy.CatalogueId);
            Assert.Null(legacy.BeatProductionPlanVersion);
            Assert.Null(legacy.BytesPurgedUtc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task TrySetDisposition_EnforcesTransitionsAndLeavesExecutionStatusUnchanged()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var image = new SceneImageRecord
            {
                SessionId = "s1", InteractionId = "i1", PromptRecordId = "p1", PromptSnapshot = "composition",
                Status = SceneImageStatus.Complete, ProductionGroupId = "group-1",
                ProductionStage = SceneImageProductionStage.Composition, Disposition = SceneImageAttemptDisposition.Active
            };
            await repo.InsertImageAsync(image);

            var shortlistedUtc = DateTime.UtcNow.AddMinutes(-1);
            Assert.True(await repo.TrySetDispositionAsync(
                image.Id, "group-1", SceneImageAttemptDisposition.Active,
                SceneImageAttemptDisposition.Shortlisted, shortlistedUtc));
            var shortlisted = await repo.GetImageAsync(image.Id);
            Assert.Equal(SceneImageAttemptDisposition.Shortlisted, shortlisted!.Disposition);
            Assert.Equal(shortlistedUtc, shortlisted.DispositionUpdatedUtc);
            Assert.Equal(SceneImageStatus.Complete, shortlisted.Status);

            Assert.False(await repo.TrySetDispositionAsync(
                image.Id, "other-group", SceneImageAttemptDisposition.Shortlisted,
                SceneImageAttemptDisposition.Rejected, DateTime.UtcNow));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.TrySetDispositionAsync(
                image.Id, "group-1", SceneImageAttemptDisposition.Shortlisted,
                SceneImageAttemptDisposition.Archived, DateTime.UtcNow));

            var rejectedUtc = DateTime.UtcNow;
            Assert.True(await repo.TrySetDispositionAsync(
                image.Id, "group-1", SceneImageAttemptDisposition.Shortlisted,
                SceneImageAttemptDisposition.Rejected, rejectedUtc));
            var rejected = await repo.GetImageAsync(image.Id);
            Assert.Equal(rejectedUtc, rejected!.DispositionUpdatedUtc);
            Assert.True(await repo.TrySetDispositionAsync(
                image.Id, "group-1", SceneImageAttemptDisposition.Rejected,
                SceneImageAttemptDisposition.Archived, DateTime.UtcNow));
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.TrySetDispositionAsync(
                image.Id, "group-1", SceneImageAttemptDisposition.Archived,
                SceneImageAttemptDisposition.Active, DateTime.UtcNow));

            var archived = await repo.GetImageAsync(image.Id);
            Assert.Equal(SceneImageAttemptDisposition.Archived, archived!.Disposition);
            Assert.Equal(SceneImageStatus.Complete, archived.Status);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ExistingLegacySceneImagesTable_MigratesProductionColumnsAsNull()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE SceneImages (
                        Id TEXT PRIMARY KEY, SessionId TEXT NOT NULL, InteractionId TEXT NOT NULL,
                        PromptRecordId TEXT NOT NULL, PromptSnapshot TEXT NOT NULL, Status TEXT NOT NULL,
                        Operation TEXT NOT NULL DEFAULT 'Generate', SourceImageId TEXT NULL,
                        EditSessionId TEXT NULL, EditCompilationAttemptId TEXT NULL,
                        EditPromptRevisionId TEXT NULL, EditIntentSnapshot TEXT NULL,
                        EditCompilerProvenanceJson TEXT NULL, FileRelativePath TEXT NULL,
                        ModelIdentifier TEXT NULL, ProviderName TEXT NULL, ContentPolicy TEXT NOT NULL,
                        ImageSize TEXT NULL, Style TEXT NULL, SettingsJson TEXT NOT NULL DEFAULT '{}',
                        ErrorMessage TEXT NULL, RegenerateOfId TEXT NULL, BeatId TEXT NULL, Pov TEXT NULL,
                        CreatedUtc TEXT NOT NULL, StartedUtc TEXT NULL, CompletedUtc TEXT NULL,
                        UpdatedUtc TEXT NOT NULL, RenderMode TEXT NOT NULL DEFAULT 'PromptOnly',
                        IdentityPackId TEXT NULL, IdentityPacksJson TEXT NULL);
                    INSERT INTO SceneImages (
                        Id, SessionId, InteractionId, PromptRecordId, PromptSnapshot, Status,
                        ContentPolicy, CreatedUtc, UpdatedUtc)
                    VALUES (
                        'legacy-image', 's1', 'i1', 'p1', 'legacy prompt', 'Complete',
                        'Unknown', $createdUtc, $createdUtc);
                    """;
                command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync();
            }

            var loaded = await repo.GetImageAsync("legacy-image");

            Assert.NotNull(loaded);
            Assert.Null(loaded!.ProductionGroupId);
            Assert.Null(loaded.ProductionStage);
            Assert.Null(loaded.Disposition);
            Assert.Null(loaded.CatalogueId);
            Assert.Null(loaded.BeatProductionPlanId);
            Assert.Null(loaded.BeatProductionPlanVersion);
            Assert.Null(loaded.MomentSetId);
            Assert.Null(loaded.MomentSetVersion);
            Assert.Null(loaded.MomentId);
            Assert.Null(loaded.MomentEnrichmentId);
            Assert.Null(loaded.MomentEnrichmentRevision);
            Assert.Null(loaded.TypedReferenceSnapshotJson);
            Assert.Null(loaded.Sha256);
            Assert.Null(loaded.BytesPurgedUtc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task InsertImage_RequiresNonEmptyPromptSnapshot()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var image = new SceneImageRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                PromptRecordId = "p1",
                PromptSnapshot = "   ",
                Status = SceneImageStatus.Pending
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => repo.InsertImageAsync(image));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpdatePromptOutput_PersistsEditedText()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var prompt = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-1",
                BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
                Pov = "Omniscient",
                Status = SceneImagePromptStatus.Complete,
                OutputPrompt = "original text"
            };
            await repo.UpsertPromptAsync(prompt);

            await repo.UpdatePromptOutputAsync(prompt.Id, "  edited text  ");

            var loaded = await repo.GetPromptAsync(prompt.Id);
            Assert.NotNull(loaded);
            Assert.Equal("edited text", loaded!.OutputPrompt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpdatePromptOutput_UnknownId_Throws()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.UpdatePromptOutputAsync("missing", "edited text"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task UpsertPrompt_RefineInstruction_RoundTrips()
    {
        var (repo, dbPath) = CreateRepo();
        try
        {
            var prompt = new SceneImagePromptRecord
            {
                SessionId = "s1",
                InteractionId = "i1",
                BeatAnalysisId = "analysis-1",
                BeatSnapshotJson = "{\"beatId\":\"beat-1\"}",
                Pov = "Omniscient",
                OutputPrompt = "a prompt",
                RefineInstruction = "more atmospheric",
                Status = SceneImagePromptStatus.Complete
            };
            await repo.UpsertPromptAsync(prompt);

            var loaded = await repo.GetPromptAsync(prompt.Id);
            Assert.NotNull(loaded);
            Assert.Equal("more atmospheric", loaded!.RefineInstruction);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + suffix)) File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
