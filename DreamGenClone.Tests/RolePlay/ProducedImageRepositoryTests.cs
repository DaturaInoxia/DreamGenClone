using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class ProducedImageRepositoryTests
{
    [Fact]
    public async Task Insert_Get_Update_And_List_RoundTrip()
    {
        var repository = CreateRepository(out var dbPath);
        try
        {
            var moment = new ProducedImage
            {
                Id = "moment-1",
                Kind = ProducedImageKind.MomentImage,
                SessionId = "session-1",
                InteractionId = "interaction-1",
                Status = ProducedImageStatus.Accepted,
                ParentImageId = "parent-1",
                VisionSource = ProducedImageVisionSource.BeatMetadata,
                VisionText = "A quiet exchange",
                PromptCompiled = "cinematic quiet exchange",
                PromptEdited = "cinematic quiet exchange at dusk",
                NegativePrompt = "blurry",
                Seed = 42,
                ModelId = "model-1",
                EndpointId = "endpoint-1",
                AppliedReferencesJson = "{\"refs\":[\"ref-1\"]}",
                IdentityStrategy = "IpAdapter",
                CostJson = "{\"credits\":2}",
                StoragePath = "session-1/moment-1.png",
                RefusalMode = SceneImageRefusalMode.None,
                ScoreJson = "{\"identity\":0.9}",
                CreatedUtc = "2026-09-05T10:00:00.0000000Z",
                UpdatedUtc = "2026-09-05T10:01:00.0000000Z"
            };
            await repository.InsertAsync(moment);

            var loaded = await repository.GetAsync(moment.Id);
            Assert.NotNull(loaded);
            Assert.Equal(moment.Id, loaded!.Id);
            Assert.Equal(moment.Kind, loaded.Kind);
            Assert.Equal(moment.SessionId, loaded.SessionId);
            Assert.Equal(moment.InteractionId, loaded.InteractionId);
            Assert.Equal(moment.Status, loaded.Status);
            Assert.Equal(moment.ParentImageId, loaded.ParentImageId);
            Assert.Equal(moment.VisionSource, loaded.VisionSource);
            Assert.Equal(moment.VisionText, loaded.VisionText);
            Assert.Equal(moment.PromptCompiled, loaded.PromptCompiled);
            Assert.Equal(moment.PromptEdited, loaded.PromptEdited);
            Assert.Equal(moment.NegativePrompt, loaded.NegativePrompt);
            Assert.Equal(moment.Seed, loaded.Seed);
            Assert.Equal(moment.ModelId, loaded.ModelId);
            Assert.Equal(moment.EndpointId, loaded.EndpointId);
            Assert.Equal(moment.AppliedReferencesJson, loaded.AppliedReferencesJson);
            Assert.Equal(moment.IdentityStrategy, loaded.IdentityStrategy);
            Assert.Equal(moment.CostJson, loaded.CostJson);
            Assert.Equal(moment.StoragePath, loaded.StoragePath);
            Assert.Equal(moment.RefusalMode, loaded.RefusalMode);
            Assert.Equal(moment.ScoreJson, loaded.ScoreJson);
            Assert.Equal(moment.CreatedUtc, loaded.CreatedUtc);
            Assert.Equal(moment.UpdatedUtc, loaded.UpdatedUtc);

            var candidate = new ProducedImage
            {
                Id = "candidate-1",
                Kind = ProducedImageKind.ReferenceCandidate,
                BatchId = "batch-1",
                TargetRef = "character-face",
                ReferenceKind = ProducedImageReferenceKind.CharacterFace,
                VisionSource = ProducedImageVisionSource.Typed,
                ParentImageId = moment.Id
            };
            await repository.InsertAsync(candidate);

            var batch = await repository.ListByBatchAsync("batch-1");
            Assert.Single(batch);
            Assert.Equal(ProducedImageStatus.Undecided, batch[0].Status);
            Assert.Equal("character-face", batch[0].TargetRef);

            candidate.Status = ProducedImageStatus.Shortlisted;
            candidate.UpdatedUtc = "2026-09-05T10:02:00.0000000Z";
            await repository.UpdateAsync(candidate);
            Assert.Equal(ProducedImageStatus.Shortlisted, (await repository.GetAsync(candidate.Id))!.Status);

            var children = await repository.ListByParentAsync(moment.Id);
            Assert.Single(children);
            Assert.Equal(candidate.Id, children[0].Id);
        }
        finally
        {
            Cleanup(dbPath);
        }
    }

    private static ProducedImageRepository CreateRepository(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), $"produced-image-repo-{Guid.NewGuid():N}.db");
        return new ProducedImageRepository(Options.Create(new PersistenceOptions
        {
            ConnectionString = $"Data Source={dbPath};Pooling=False"
        }));
    }

    private static void Cleanup(string dbPath)
    {
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(dbPath + suffix); } catch { }
        }
    }
}
