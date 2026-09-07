using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProducedImageRepositoryQueryTests
{
    [Fact]
    public async Task QueryAsync_WithoutFacets_ReturnsAllNewestFirst()
    {
        var (dbPath, repository) = CreateRepository();
        try
        {
            var images = CreateImages();
            foreach (var image in images) await repository.InsertAsync(image);

            var page = await repository.QueryAsync(new ProducedImageQuery());

            Assert.Equal(images.Count, page.TotalCount);
            Assert.Equal(images.OrderByDescending(image => image.CreatedUtc).Select(image => image.Id),
                page.Items.Select(image => image.Id));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync_FilteredByStatus_ReturnsMatchingRowsAndCount()
    {
        var (dbPath, repository) = CreateRepository();
        try
        {
            foreach (var image in CreateImages()) await repository.InsertAsync(image);

            var page = await repository.QueryAsync(new ProducedImageQuery { Status = ProducedImageStatus.Accepted });

            Assert.Equal(2, page.TotalCount);
            Assert.Equal(new[] { "image-5", "image-2" }, page.Items.Select(image => image.Id));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync_WithPaging_ReturnsPageAndUnpagedCount()
    {
        var (dbPath, repository) = CreateRepository();
        try
        {
            foreach (var image in CreateImages()) await repository.InsertAsync(image);

            var page = await repository.QueryAsync(new ProducedImageQuery { Take = 2, Skip = 1 });

            Assert.Equal(5, page.TotalCount);
            Assert.Equal(new[] { "image-4", "image-3" }, page.Items.Select(image => image.Id));
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    [Fact]
    public async Task QueryAsync_WithNonPositiveTake_Throws()
    {
        var (dbPath, repository) = CreateRepository();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repository.QueryAsync(new ProducedImageQuery { Take = 0 }));

            Assert.Equal("Produced image query Take must be positive.", exception.Message);
        }
        finally
        {
            DeleteDatabase(dbPath);
        }
    }

    private static (string DbPath, ProducedImageRepository Repository) CreateRepository()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"produced-image-query-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        });
        return (dbPath, new ProducedImageRepository(options));
    }

    private static List<ProducedImage> CreateImages() =>
    [
        CreateImage("image-1", "2026-09-05T00:00:01.0000000Z", ProducedImageKind.ReferenceCandidate,
            ProducedImageStatus.Undecided, "batch-a", "model-a"),
        CreateImage("image-2", "2026-09-05T00:00:02.0000000Z", ProducedImageKind.ReferenceCandidate,
            ProducedImageStatus.Accepted, "batch-a", "model-b"),
        CreateImage("image-3", "2026-09-05T00:00:03.0000000Z", ProducedImageKind.EditAttempt,
            ProducedImageStatus.Rejected, "batch-b", "model-a"),
        CreateImage("image-4", "2026-09-05T00:00:04.0000000Z", ProducedImageKind.MomentImage,
            ProducedImageStatus.Shortlisted, "batch-b", "model-c"),
        CreateImage("image-5", "2026-09-05T00:00:05.0000000Z", ProducedImageKind.PromotedReference,
            ProducedImageStatus.Accepted, "batch-c", "model-c")
    ];

    private static ProducedImage CreateImage(
        string id,
        string createdUtc,
        ProducedImageKind kind,
        ProducedImageStatus status,
        string batchId,
        string modelId) => new()
        {
            Id = id,
            Kind = kind,
            Status = status,
            ReferenceKind = ProducedImageReferenceKind.CharacterFace,
            BatchId = batchId,
            TargetRef = $"target-{batchId}",
            ModelId = modelId,
            VisionSource = ProducedImageVisionSource.Typed,
            RefusalMode = SceneImageRefusalMode.None,
            CreatedUtc = createdUtc,
            UpdatedUtc = createdUtc
        };

    private static void DeleteDatabase(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { }
        }
    }
}