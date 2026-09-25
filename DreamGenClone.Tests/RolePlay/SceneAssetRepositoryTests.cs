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

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
