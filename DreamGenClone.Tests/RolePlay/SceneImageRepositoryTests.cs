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
