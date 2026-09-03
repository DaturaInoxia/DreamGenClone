using System.Text.Json;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageRenderingJobHandlerTests
{
    [Fact]
    public async Task HandleAsync_CompletedImage_IsIdempotentAndSkipsAllCollaborators()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"scene-image-render-handler-{Guid.NewGuid():N}.db");
        var repository = new SceneImageRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={databasePath}"
        }));
        var image = new SceneImageRecord
        {
            Id = "image-complete",
            SessionId = "session-1",
            InteractionId = "interaction-1",
            PromptRecordId = "prompt-1",
            PromptSnapshot = "already rendered",
            Status = SceneImageStatus.Complete,
            FileRelativePath = "session-1/image-complete.png",
            Sha256 = "EXISTINGHASH",
            CompletedUtc = DateTime.UtcNow
        };

        try
        {
            await repository.InsertImageAsync(image);
            var handler = new SceneImageRenderingJobHandler(
                repository,
                storage: null!,
                modelResolutionService: null!,
                imageClient: null!,
                identityClient: null!,
                identityRequestCompiler: null!,
                compilerRegistry: null!,
                debugEventSink: null!,
                NullLogger<SceneImageRenderingJobHandler>.Instance);
            var job = new BackgroundJobEnvelope
            {
                JobType = BackgroundJobTypes.SceneImageRendering,
                PayloadJson = JsonSerializer.Serialize(new SceneImageRenderingJobPayload
                {
                    SessionId = image.SessionId,
                    InteractionId = image.InteractionId,
                    ImageRecordId = image.Id
                })
            };

            await handler.HandleAsync(job, CancellationToken.None);

            var persisted = await repository.GetImageAsync(image.Id);
            Assert.NotNull(persisted);
            Assert.Equal(SceneImageStatus.Complete, persisted!.Status);
            Assert.Equal("EXISTINGHASH", persisted.Sha256);
            Assert.Equal("session-1/image-complete.png", persisted.FileRelativePath);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                try
                {
                    if (File.Exists(databasePath + suffix))
                        File.Delete(databasePath + suffix);
                }
                catch
                {
                }
            }
        }
    }
}