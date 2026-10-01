using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneAssetRepositoryTests
{
    [Fact]
    public async Task Upsert_Get_RoundTripsMetadata()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            var asset = new SceneAsset
            {
                Id = "a1",
                Name = "Forest clearing",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Prompt = "a misty forest clearing",
                FileRelativePath = "assets/a1.png",
                MediaType = "image/png",
                Width = 1024,
                Height = 1024,
                ByteLength = 123,
                Sha256 = "ABCD",
                FaceView = SceneImageReferenceFaceView.Front,
                IdentityPackId = "pack-1",
                CharacterProfileId = "char-1",
                Type = SceneAssetType.CharacterFace,
                AssociationMetadataJson = "{\"role\":\"lead\"}",
                SourceApprovalDecisionId = "decision-1",
                SourceSceneImageId = "image-1",
                SourceSha256 = "ABCD",
                SourceProvenanceJson = "{\"source\":\"approval\"}",
                CompletedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            await repo.UpsertAsync(asset);

            var loaded = await repo.GetAsync("a1");
            Assert.NotNull(loaded);
            Assert.Equal("Forest clearing", loaded!.Name);
            Assert.Equal(SceneAssetKind.PromptGenerated, loaded.Kind);
            Assert.Equal(SceneAssetStatus.Complete, loaded.Status);
            Assert.Equal("assets/a1.png", loaded.FileRelativePath);
            Assert.Equal(1024, loaded.Width);
            Assert.Equal(SceneImageReferenceFaceView.Front, loaded.FaceView);
            Assert.Equal("pack-1", loaded.IdentityPackId);
            Assert.Equal("char-1", loaded.CharacterProfileId);
            Assert.Equal(SceneAssetType.CharacterFace, loaded.Type);
            Assert.Equal("{\"role\":\"lead\"}", loaded.AssociationMetadataJson);
            Assert.Equal("decision-1", loaded.SourceApprovalDecisionId);
            Assert.Equal("image-1", loaded.SourceSceneImageId);
            Assert.Equal("ABCD", loaded.SourceSha256);
            Assert.Equal("{\"source\":\"approval\"}", loaded.SourceProvenanceJson);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "old", Name = "Old", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, CreatedUtc = DateTime.UtcNow.AddHours(-2) });
            await repo.UpsertAsync(new SceneAsset { Id = "new", Name = "New", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, CreatedUtc = DateTime.UtcNow });

            var all = await repo.ListAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal("new", all[0].Id);
            Assert.Equal("old", all[1].Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task ListByPack_FiltersToPack()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "p1f", Name = "Front", Kind = SceneAssetKind.ProfilePackFront, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-1", CharacterProfileId = "char-1" });
            await repo.UpsertAsync(new SceneAsset { Id = "p1e", Name = "3/4", Kind = SceneAssetKind.ProfilePackFace, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-1", CharacterProfileId = "char-1" });
            await repo.UpsertAsync(new SceneAsset { Id = "other", Name = "Other", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, IdentityPackId = "pack-2" });

            var byPack = await repo.ListByPackAsync("pack-1");
            Assert.Equal(2, byPack.Count);
            Assert.DoesNotContain(byPack, a => a.Id == "other");
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Upsert_Update_PersistsNewStatus()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            var asset = new SceneAsset { Id = "a1", Name = "X", Kind = SceneAssetKind.PromptGenerated, Status = SceneAssetStatus.Pending };
            await repo.UpsertAsync(asset);

            asset.Status = SceneAssetStatus.Complete;
            asset.FileRelativePath = "assets/a1.png";
            asset.UpdatedUtc = DateTime.UtcNow;
            await repo.UpsertAsync(asset);

            var loaded = await repo.GetAsync("a1");
            Assert.Equal(SceneAssetStatus.Complete, loaded!.Status);
            Assert.Equal("assets/a1.png", loaded.FileRelativePath);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "a1", Name = "X", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete });
            await repo.DeleteAsync("a1");
            Assert.Null(await repo.GetAsync("a1"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task CountByFilePath_CountsSharedFiles()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset { Id = "a1", Name = "A", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, FileRelativePath = "assets/shared.png" });
            await repo.UpsertAsync(new SceneAsset { Id = "a2", Name = "B", Kind = SceneAssetKind.Uploaded, Status = SceneAssetStatus.Complete, FileRelativePath = "assets/shared.png" });

            Assert.Equal(2, await repo.CountByFilePathAsync("assets/shared.png"));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task Asset_CanOwnMultipleIndependentImages()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset
            {
                Id = "asset-1",
                Name = "Forest clearing",
                Type = SceneAssetType.Location,
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Pending
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "image-1",
                AssetId = "asset-1",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = "assets/image-1.png"
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "image-2",
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Pending,
                SourceImageId = "image-1",
                Prompt = "add morning fog"
            });

            var images = await repo.ListImagesAsync("asset-1");

            Assert.Equal(2, images.Count(image => image.Id is "image-1" or "image-2"));
            Assert.All(images.Where(image => image.Id is "image-1" or "image-2"), image => Assert.Equal("asset-1", image.AssetId));
            Assert.Equal("image-1", images.Single(image => image.Id == "image-2").SourceImageId);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The seed that produced an image is PERSISTED, so a keeper can be reproduced instead of lost. Recorded whichever
    /// way the seed was chosen: a run can pin one (a catalog position declares a seed) or let the render draw a fresh
    /// one, and in both cases the number that reached the sampler is what makes the image repeatable.
    /// </summary>
    [Fact]
    public async Task Image_RecordsTheSeedThatProducedIt()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset
            {
                Id = "asset-1",
                Name = "Forest clearing",
                Type = SceneAssetType.Location,
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Pending
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "image-1",
                AssetId = "asset-1",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Prompt = "a photorealistic scene",
                Seed = 20311
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "image-2",
                AssetId = "asset-1",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete
            });

            var images = await repo.ListImagesAsync("asset-1");

            Assert.Equal(20311L, images.Single(image => image.Id == "image-1").Seed!.Value);
            // Null is "not recorded" — an uploaded image, or a row written before the seed was kept. It is never
            // "no seed", because every render has one.
            Assert.Null(images.Single(image => image.Id == "image-2").Seed);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// Operator report 2026-09-24: the uploaded front candidate could not be deleted at all — "SQLite Error 19:
    /// FOREIGN KEY constraint failed" — because <c>SceneAssetImageEditSessions.SourceImageId</c> references the image
    /// ON DELETE RESTRICT and the delete only detached the derived <c>SceneAssetImages</c> rows. Every source of a
    /// recorded operation was therefore held forever by the record of that operation. The delete now clears that
    /// history in the same transaction and keeps what the sessions PRODUCED (their own provenance carries the source
    /// checksum, which is what the canonical-front lineage walk reads).
    /// </summary>
    [Fact]
    public async Task DeleteImageAsync_ClearsTheEditSessionsThatConsumedTheImage_AndKeepsWhatTheyProduced()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset
            {
                Id = "asset-1",
                Name = "Becky front",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "upload",
                AssetId = "asset-1",
                Kind = SceneAssetKind.Uploaded,
                Status = SceneAssetStatus.Complete,
                FileRelativePath = "assets/upload.png"
            });
            await repo.UpsertImageAsync(new SceneAssetImage
            {
                Id = "edited",
                AssetId = "asset-1",
                Kind = SceneAssetKind.Edited,
                Status = SceneAssetStatus.Complete,
                SourceImageId = "upload",
                FileRelativePath = "assets/edited.png"
            });

            await SeedEditHistoryAsync(dbPath, sourceImageId: "upload");

            // Prove the constraint is enforced here: a direct row delete must fail. Without this probe the test
            // would also pass against a schema that enforces nothing, which is exactly how the defect survived.
            await AssertDirectDeleteIsBlockedAsync(dbPath, "upload");

            await repo.DeleteImageAsync("upload");

            Assert.Null(await repo.GetImageAsync("upload"));
            var edited = await repo.GetImageAsync("edited");
            Assert.NotNull(edited);
            Assert.Null(edited!.SourceImageId);
            Assert.Equal(0, await CountEditHistoryAsync(dbPath));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// Creates the asset-image edit tables with the RESTRICTed foreign key the live schema has, plus one session,
    /// its compilation attempt and its prompt revision, all keyed by the image under test.
    /// </summary>
    private static async Task SeedEditHistoryAsync(string dbPath, string sourceImageId)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditSessions (
                    Id TEXT PRIMARY KEY, AssetId TEXT NOT NULL, SourceImageId TEXT NOT NULL, SourceImageSha256 TEXT NOT NULL,
                    Status TEXT NOT NULL, DescriptionText TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NULL,
                    FOREIGN KEY (SourceImageId) REFERENCES SceneAssetImages(Id) ON DELETE RESTRICT);
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditCompilationAttempts (
                    Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL, RawIntent TEXT NOT NULL,
                    Status TEXT NOT NULL, CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY (EditSessionId) REFERENCES SceneAssetImageEditSessions(Id) ON DELETE RESTRICT);
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditPromptRevisions (
                    Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL, Prompt TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    FOREIGN KEY (CompilationAttemptId) REFERENCES SceneAssetImageEditCompilationAttempts(Id) ON DELETE RESTRICT);
                """;
            await schema.ExecuteNonQueryAsync();
        }

        await using var rows = connection.CreateCommand();
        rows.CommandText = """
            INSERT INTO SceneAssetImageEditSessions
                (Id, AssetId, SourceImageId, SourceImageSha256, Status, CreatedUtc, UpdatedUtc)
            VALUES ('session-1', 'asset-1', $source, 'SHA', 'Completed',
                    '2026-09-24T01:11:25.0000000Z', '2026-09-24T01:11:25.0000000Z');
            INSERT INTO SceneAssetImageEditCompilationAttempts
                (Id, EditSessionId, Ordinal, RawIntent, Status, CreatedUtc)
            VALUES ('attempt-1', 'session-1', 0, 'remove the background', 'Completed', '2026-09-24T01:11:25.0000000Z');
            INSERT INTO SceneAssetImageEditPromptRevisions
                (Id, CompilationAttemptId, Ordinal, Prompt, CreatedUtc)
            VALUES ('revision-1', 'attempt-1', 0, 'remove the background', '2026-09-24T01:11:25.0000000Z');
            """;
        rows.Parameters.AddWithValue("$source", sourceImageId);
        await rows.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Deletes the image row directly, bypassing the repository, and requires the foreign key to refuse it. This is
    /// the condition that made the operator's delete fail ("SQLite Error 19: FOREIGN KEY constraint failed"); a schema
    /// or connection that does not enforce it would make the cascade test above prove nothing.
    /// </summary>
    private static async Task AssertDirectDeleteIsBlockedAsync(string dbPath, string imageId)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SceneAssetImages WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", imageId);
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }

    private static async Task<int> CountEditHistoryAsync(string dbPath)
    {
        await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM SceneAssetImageEditSessions)
                 + (SELECT COUNT(*) FROM SceneAssetImageEditCompilationAttempts)
                 + (SELECT COUNT(*) FROM SceneAssetImageEditPromptRevisions);
            """;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static SceneAssetRepository CreateRepoAsync(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"scene-asset-repo-{Guid.NewGuid():N}.db");
        var repo = new SceneAssetRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        }));
        // Touch the schema by running a read against a missing row (no row is inserted).
        repo.GetAsync("__schema_probe__").GetAwaiter().GetResult();
        return repo;
    }

    // ------------------------------------------------------------------ prompt compilation (wardrobe items, B-134)

    /// <summary>
    /// A compiled prompt is written onto an EXISTING row by its own narrow UPDATE, because the ordinary upsert
    /// deliberately never touches Prompt: a stored prompt is what an image WAS made from. The wardrobe tab depends on
    /// both halves of that split - the row exists as soon as the operator asks (so there is something to see and
    /// something to fail into), and the prompt lands on it once it has been drafted.
    /// </summary>
    [Fact]
    public async Task SetImagePromptAsync_WritesPromptAndCompilerOntoTheExistingRow_AndTheUpsertCannot()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset
            {
                Id = "a1", Name = "Dress", Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Pending, Type = SceneAssetType.Wardrobe
            });
            var image = new SceneAssetImage
            {
                Id = "img-1", AssetId = "a1", Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Pending, Prompt = "a yellow sundress",
                MediaType = "image/png", AssociationMetadataJson = "{\"stage\":\"compiling\"}"
            };
            await repo.UpsertImageAsync(image);

            // The ordinary upsert must NOT rewrite a stored prompt.
            image.Prompt = "changed by an ordinary save";
            await repo.UpsertImageAsync(image);
            Assert.Equal("a yellow sundress", (await repo.GetImageAsync("img-1"))!.Prompt);

            await repo.SetImagePromptAsync(
                "img-1",
                "A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface.",
                "wardrobe-item-qwen-image-21-natural-language",
                negativePrompt: null,
                associationMetadataJson: "{\"stage\":\"compiled\"}");

            var loaded = (await repo.GetImageAsync("img-1"))!;
            Assert.Equal("A product photograph of a yellow cotton sundress laid flat on a plain light-grey surface.", loaded.Prompt);
            Assert.Equal("wardrobe-item-qwen-image-21-natural-language", loaded.PromptCompilerId);
            Assert.Null(loaded.NegativePrompt);
            Assert.Equal("{\"stage\":\"compiled\"}", loaded.AssociationMetadataJson);
            Assert.Equal(SceneAssetStatus.Pending, loaded.Status);
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
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }

    // ------------------------------------------------------------------ character pose assets (B-130 §D8)

    /// <summary>
    /// A character pose asset is a SceneAsset with its own type, so it round-trips through the SAME store - no second
    /// table, no parallel lifecycle, and no new approval rules to keep in step with the existing ones.
    /// </summary>
    [Fact]
    public async Task CharacterPose_RoundTripsThroughTheAssetStore()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            await repo.UpsertAsync(new SceneAsset
            {
                Id = "pose-1",
                Name = "Becky - all fours",
                Kind = SceneAssetKind.PromptGenerated,
                Status = SceneAssetStatus.Complete,
                Type = SceneAssetType.CharacterPose,
                CharacterProfileId = "becky",
                // Which pose rides the view descriptor: the picker filters on the character and the built state, both of
                // which already have typed columns, so the pose itself is a label rather than a query axis (B-130 D8).
                ViewDescriptorJson = "{\"poseKey\":\"allfours\"}",
                BodyState = SceneImageReferenceBodyState.Clothed,
                BodyView = SceneImageReferenceBodyView.Back,
                FileRelativePath = "assets/pose-1.png",
                UpdatedUtc = DateTime.UtcNow
            });

            var loaded = await repo.GetAsync("pose-1");

            Assert.NotNull(loaded);
            Assert.Equal(SceneAssetType.CharacterPose, loaded!.Type);
            Assert.Equal("becky", loaded.CharacterProfileId);
            Assert.Equal("{\"poseKey\":\"allfours\"}", loaded.ViewDescriptorJson);
            Assert.Equal(SceneImageReferenceBodyState.Clothed, loaded.BodyState);
            Assert.Equal(SceneImageReferenceBodyView.Back, loaded.BodyView);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// The new type is APPENDED, so no stored row may change meaning. Renumbering the enum is what would silently
    /// reinterpret a whole table, so this asserts the persisted NAME for an existing row, not just a round trip.
    /// </summary>
    [Fact]
    public async Task AppendingCharacterPose_DoesNotReinterpretExistingAssetTypes()
    {
        var repo = CreateRepoAsync(out var dbPath);
        try
        {
            var existing = new[]
            {
                (Id: "a-face", Type: SceneAssetType.CharacterFace),
                (Id: "a-body", Type: SceneAssetType.CharacterBody),
                (Id: "a-wardrobe", Type: SceneAssetType.Wardrobe),
                (Id: "a-location", Type: SceneAssetType.Location)
            };
            foreach (var item in existing)
            {
                await repo.UpsertAsync(new SceneAsset
                {
                    Id = item.Id,
                    Name = item.Id,
                    Kind = SceneAssetKind.PromptGenerated,
                    Status = SceneAssetStatus.Complete,
                    Type = item.Type,
                    UpdatedUtc = DateTime.UtcNow
                });
            }

            foreach (var item in existing)
            {
                var loaded = await repo.GetAsync(item.Id);
                Assert.Equal(item.Type, loaded!.Type);
            }

            await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Type FROM SceneAssets WHERE Id = 'a-location';";
            Assert.Equal("Location", Convert.ToString(await command.ExecuteScalarAsync()));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }
}
