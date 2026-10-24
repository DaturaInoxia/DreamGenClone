using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-160: the location analysis survives storage, and an ordinary save of an image row cannot touch it.
///
/// <para>
/// Two failure modes are pinned here. The first is a SILENT one: the image reader is ordinal, so a column appended in
/// the wrong place would shift every field after it onto the wrong property — the schema, the SELECT list and the
/// reader have to agree even though nothing at compile time enforces it. The second is a LOSS one: the analysis is
/// written by one job and read by a later render, so if an ordinary image save carried these columns it could wipe a
/// reading or resurrect a superseded one.
/// </para>
/// </summary>
public sealed class LocationImageAnalysisStorageTests
{
    private const string Sha = "SHA-ONE";

    /// <summary>The migration adds the three columns to an image table, which is how a live database gets them.</summary>
    [Fact]
    public async Task TheSchemaMigrationAddsTheAnalysisColumns()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);

            await using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(*) FROM pragma_table_info('SceneAssetImages') WHERE name IN "
                + "('LocationAnalysisJson','LocationAnalyzedSha256','LocationAnalyzedUtc');";
            Assert.Equal(3, Convert.ToInt32(await command.ExecuteScalarAsync()));

            // ...and a row that has never been analysed reads back with the columns NULL, rather than a default that
            // would look like a reading.
            var read = await repository.GetImageAsync(imageId);
            Assert.NotNull(read);
            Assert.Null(read!.LocationAnalysisJson);
            Assert.Null(read.LocationAnalyzedSha256);
            Assert.Null(read.LocationAnalyzedUtc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task AnAnalysisIsStoredAndReadBackAgainstItsChecksum()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);
            var analysis = LocationAnchorPromptBuilder.Parse(
                """{ "description": "Two trailers face a mown yard.", "anchors": [ { "role": "Deck", "name": "the right-hand trailer's deck", "horizontal": "Right" } ] }""",
                Sha,
                new DateTime(2026, 10, 13, 16, 0, 0, DateTimeKind.Utc));

            await repository.SaveAsync(imageId, analysis);

            var read = await repository.GetImageAsync(imageId);
            var restored = LocationImageAnalysisJson.Read(read!.LocationAnalysisJson);

            Assert.Equal(analysis, restored);
            Assert.Equal(Sha, read.LocationAnalyzedSha256);
            Assert.NotNull(read.LocationAnalyzedUtc);
            Assert.True(restored.IsAbout(read.Sha256));
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>A re-analysis REPLACES the stored reading, which is what makes "analyse again after the image changed" work.</summary>
    [Fact]
    public async Task AReAnalysisReplacesTheStoredReading()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);
            await repository.SaveAsync(imageId, Analysis("SHA-ONE", "the first reading"));
            await repository.SaveAsync(imageId, Analysis("SHA-TWO", "the second reading"));

            var read = await repository.GetImageAsync(imageId);
            var restored = LocationImageAnalysisJson.Read(read!.LocationAnalysisJson);

            Assert.Equal("SHA-TWO", restored.AnalyzedSha256);
            Assert.Equal("the second reading", restored.Description);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// An ordinary save of the image row leaves the analysis alone — the LOSS the narrow store contract exists to
    /// prevent, because the analysis is written by its own job and consumed by a later render.
    /// </summary>
    [Fact]
    public async Task AnOrdinaryImageSaveDoesNotTouchTheAnalysis()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);
            await repository.SaveAsync(imageId, Analysis(Sha, "the reading"));

            var loaded = await repository.GetImageAsync(imageId);
            // A field the upsert DOES own. DisplayName and TagsJson are deliberately NOT updated by an ordinary save
            // (each has its own writer, so a completion cannot clobber a name the operator typed), so using one of
            // them here would have asserted the opposite of the code's intent.
            loaded!.NegativePrompt = "changed by the ordinary save";
            await repository.UpsertImageAsync(loaded);

            var read = await repository.GetImageAsync(imageId);
            Assert.Equal("the reading", LocationImageAnalysisJson.Read(read!.LocationAnalysisJson).Description);
            Assert.Equal(Sha, read.LocationAnalyzedSha256);

            // ...and the ordinary save really did happen, so this is not passing because nothing was written.
            Assert.Equal("changed by the ordinary save", read.NegativePrompt);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>
    /// A stored analysis is read back onto the RIGHT properties, which the ordinal reader cannot guarantee on its own:
    /// a column appended out of step would shift every field after it.
    /// </summary>
    [Fact]
    public async Task TheReaderKeepsEveryFieldOnItsOwnPropertyAfterTheNewColumns()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);
            await repository.SaveAsync(imageId, Analysis(Sha, "the reading"));

            var read = await repository.GetImageAsync(imageId);

            Assert.Equal(Sha, read!.Sha256);
            Assert.Equal("prompt", read.Prompt);
            Assert.Equal(SceneAssetKind.PromptGenerated, read.Kind);
            Assert.Equal(SceneAssetStatus.Complete, read.Status);
            Assert.Equal($"assets/{imageId}.png", read.FileRelativePath);
            Assert.Equal("image/png", read.MediaType);
            Assert.Equal(1024, read.Width);
            Assert.Equal(1024, read.Height);
            Assert.Equal(1024, read.ByteLength);
            Assert.Equal(SceneAssetProductionApprovalStatus.Draft, read.ProductionApprovalStatus);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    /// <summary>Clearing is a real state: "no longer analysed" must be recordable, not merely unreachable.</summary>
    [Fact]
    public async Task AnAnalysisCanBeCleared()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var imageId = await SeedImageAsync(repository, Sha);
            await repository.SaveAsync(imageId, Analysis(Sha, "the reading"));
            await repository.SaveAsync(imageId, null);

            var read = await repository.GetImageAsync(imageId);
            Assert.Null(read!.LocationAnalysisJson);
            Assert.Null(read.LocationAnalyzedSha256);
            Assert.Null(read.LocationAnalyzedUtc);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    [Fact]
    public async Task SavingAnAnalysisForAnUnknownImageFailsFast()
    {
        var (repository, dbPath) = CreateRepository();
        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.SaveAsync("no-such-image", Analysis(Sha, "the reading")));

            Assert.Contains("no-such-image", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static LocationImageAnalysis Analysis(string sha, string description) =>
        new(description, [new LocationAnchor(LocationAnchorRole.Lawn, "the mown yard")], sha, DateTime.UtcNow);

    private static (SceneAssetRepository Repository, string DbPath) CreateRepository()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dgc-location-analysis-{Guid.NewGuid():N}.db");
        var repository = new SceneAssetRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        }));

        // Touching the schema, so the first assertion is about the migrated table and not about lazy creation.
        repository.GetImageAsync("__schema_probe__").GetAwaiter().GetResult();
        return (repository, dbPath);
    }

    private static async Task<string> SeedImageAsync(SceneAssetRepository repository, string sha256)
    {
        var assetId = $"yard-{Guid.NewGuid():N}";
        await repository.UpsertAsync(new SceneAsset
        {
            Id = assetId,
            Name = assetId,
            Kind = SceneAssetKind.Uploaded,
            Status = SceneAssetStatus.Complete,
            Type = SceneAssetType.Location,
            IsContainerOnly = true
        });

        var imageId = Guid.NewGuid().ToString("N");
        await repository.UpsertImageAsync(new SceneAssetImage
        {
            Id = imageId,
            AssetId = assetId,
            Kind = SceneAssetKind.PromptGenerated,
            Status = SceneAssetStatus.Complete,
            Prompt = "prompt",
            FileRelativePath = $"assets/{imageId}.png",
            MediaType = "image/png",
            Width = 1024,
            Height = 1024,
            ByteLength = 1024,
            Sha256 = sha256,
            ProductionApprovalStatus = SceneAssetProductionApprovalStatus.Draft
        });

        return imageId;
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { /* best effort */ }
        }
    }
}
