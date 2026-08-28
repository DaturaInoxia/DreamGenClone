using System.Security.Cryptography;
using System.Text;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageEditRepositoryTests
{
    private const string SourceSha = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task ProviderAndModel_MultimodalConfigurationRoundTrips()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var provider = new Provider
            {
                Name = "Qwen VL",
                BaseUrl = "https://vision.internal",
                LifecycleStrategyIdentifier = "ScheduledSinglePod",
                ReadinessPath = "/v1/models",
                ReadinessSuccessContractJson = "{\"model\":\"qwen-vl\"}",
                TransitionTimeoutSeconds = 500,
                TransitionMarginSeconds = 60,
                ShutdownDrainPolicyJson = "{\"mode\":\"drain\"}",
                MaximumActiveRequests = 1,
                QueueCapacity = 8,
                CredentialReference = "secret:qwen-vl",
                ServerIdentityPolicyJson = "{\"tls\":true}",
                AllowedNetworkBoundary = "private"
            };
            await fixture.ProviderRepository.SaveAsync(provider);

            var model = new RegisteredModel
            {
                ProviderId = provider.Id,
                ModelIdentifier = "qwen2.5-vl-7b-edit-compiler",
                DisplayName = "Qwen VL Edit Compiler",
                SupportsImageInput = true,
                MaximumInputImages = 1,
                MaximumInputImageBytes = 10 * 1024 * 1024,
                MaximumInputImagePixels = 1_048_576,
                MaximumInputImageDimension = 2048,
                AcceptedInputMediaTypes = "image/png,image/jpeg,image/webp",
                MaximumResponseBytes = 1_048_576,
                RuntimeRevision = "vllm-0.27.1",
                ArtifactRevision = "536a35794df8831aa814970ee8f89eff577e7718"
            };
            await fixture.ModelRepository.SaveAsync(model);

            var loadedProvider = await fixture.ProviderRepository.GetByIdAsync(provider.Id);
            var loadedModel = await fixture.ModelRepository.GetByIdAsync(model.Id);

            Assert.Equal("ScheduledSinglePod", loadedProvider!.LifecycleStrategyIdentifier);
            Assert.Equal(500, loadedProvider.TransitionTimeoutSeconds);
            Assert.Equal(60, loadedProvider.TransitionMarginSeconds);
            Assert.Equal("secret:qwen-vl", loadedProvider.CredentialReference);
            Assert.True(loadedModel!.SupportsImageInput);
            Assert.Equal(1, loadedModel.MaximumInputImages);
            Assert.Equal(1_048_576, loadedModel.MaximumInputImagePixels);
            Assert.Equal("vllm-0.27.1", loadedModel.RuntimeRevision);
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    [Fact]
    public async Task ReadyLatestRevision_RoundTripsAndIsExecutable()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = CreateSession();
            await fixture.EditRepository.CreateSessionAsync(session);
            var attempt = CreateAttempt(session.Id, 0);
            await fixture.EditRepository.CreateAttemptAsync(attempt);

            attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
            attempt.StartedUtc = DateTime.UtcNow;
            await fixture.EditRepository.UpdateAttemptAsync(attempt);
            attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
            attempt.RawModelResponse = "{\"status\":\"ready\"}";
            attempt.ParsedResultJson = attempt.RawModelResponse;
            attempt.CompletedUtc = DateTime.UtcNow;
            await fixture.EditRepository.UpdateAttemptAsync(attempt);

            var revision = CreateRevision(attempt.Id, 0, "Change the shirt to red.");
            await fixture.EditRepository.CreateRevisionAsync(revision);
            var executable = await fixture.EditRepository.GetExecutableRevisionAsync(
                session.Id, attempt.Id, revision.Id, SourceSha, revision.PromptSha256);

            Assert.Equal(revision.Id, executable.Id);
            Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, (await fixture.EditRepository.GetAttemptAsync(attempt.Id))!.Status);
            Assert.Single(await fixture.EditRepository.ListRevisionsAsync(attempt.Id));
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    [Fact]
    public async Task AttemptOrdinalAndStatusTransitions_AreStrict()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = CreateSession();
            await fixture.EditRepository.CreateSessionAsync(session);
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.EditRepository.CreateAttemptAsync(CreateAttempt(session.Id, 1)));

            var attempt = CreateAttempt(session.Id, 0);
            await fixture.EditRepository.CreateAttemptAsync(attempt);
            attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.EditRepository.UpdateAttemptAsync(attempt));
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    [Fact]
    public async Task NewAttempt_MakesPriorReadyRevisionStale()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = CreateSession();
            await fixture.EditRepository.CreateSessionAsync(session);
            var attempt = CreateAttempt(session.Id, 0);
            await MakeReadyAsync(fixture.EditRepository, attempt);
            var revision = CreateRevision(attempt.Id, 0, "Change the shirt to red.");
            await fixture.EditRepository.CreateRevisionAsync(revision);
            await fixture.EditRepository.CreateAttemptAsync(CreateAttempt(session.Id, 1));

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.EditRepository.GetExecutableRevisionAsync(
                session.Id, attempt.Id, revision.Id, SourceSha, revision.PromptSha256));
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    [Fact]
    public async Task ReferencedEditRecords_CannotBeDeleted()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var source = new SceneImageRecord
            {
                SessionId = "rp-session",
                InteractionId = "interaction",
                PromptRecordId = "prompt",
                PromptSnapshot = "source prompt",
                Status = SceneImageStatus.Complete
            };
            await fixture.ImageRepository.InsertImageAsync(source);
            var session = CreateSession(source.Id);
            await fixture.EditRepository.CreateSessionAsync(session);
            var attempt = CreateAttempt(session.Id, 0);
            await MakeReadyAsync(fixture.EditRepository, attempt);
            var revision = CreateRevision(attempt.Id, 0, "Change the shirt to red.");
            await fixture.EditRepository.CreateRevisionAsync(revision);
            var editedImage = new SceneImageRecord
            {
                SessionId = source.SessionId,
                InteractionId = source.InteractionId,
                PromptRecordId = source.PromptRecordId,
                PromptSnapshot = revision.Prompt,
                Status = SceneImageStatus.Pending,
                Operation = SceneImageOperation.Edit,
                SourceImageId = source.Id,
                EditSessionId = session.Id,
                EditCompilationAttemptId = attempt.Id,
                EditPromptRevisionId = revision.Id,
                EditIntentSnapshot = attempt.RawIntent,
                EditCompilerProvenanceJson = "{\"schemaVersion\":\"1\"}"
            };
            await fixture.ImageRepository.InsertImageAsync(editedImage);

            var loadedImage = await fixture.ImageRepository.GetImageAsync(editedImage.Id);
            Assert.Equal(session.Id, loadedImage!.EditSessionId);
            Assert.Equal(attempt.Id, loadedImage.EditCompilationAttemptId);
            Assert.Equal(revision.Id, loadedImage.EditPromptRevisionId);
            Assert.Equal(attempt.RawIntent, loadedImage.EditIntentSnapshot);
            Assert.Equal("{\"schemaVersion\":\"1\"}", loadedImage.EditCompilerProvenanceJson);

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.EditRepository.DeleteRevisionAsync(revision.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.EditRepository.DeleteAttemptAsync(attempt.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.EditRepository.DeleteSessionAsync(session.Id));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImageRepository.DeleteImageAsync(source.Id));
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    private static async Task MakeReadyAsync(SceneImageEditRepository repository, SceneImageEditCompilationAttempt attempt)
    {
        await repository.CreateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        await repository.UpdateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
        await repository.UpdateAttemptAsync(attempt);
    }

    [Fact]
    public async Task SetDescriptionAsync_PersistsAndRoundsTrips()
    {
        var fixture = await CreateFixtureAsync();
        try
        {
            var session = CreateSession();
            await fixture.EditRepository.CreateSessionAsync(session);

            var loaded = await fixture.EditRepository.GetSessionAsync(session.Id);
            Assert.NotNull(loaded);
            Assert.Null(loaded.DescriptionText);

            await fixture.EditRepository.SetDescriptionAsync(session.Id, "A woman holding an object close to her mouth.", DateTime.UtcNow);
            loaded = await fixture.EditRepository.GetSessionAsync(session.Id);
            Assert.NotNull(loaded);
            Assert.Equal("A woman holding an object close to her mouth.", loaded.DescriptionText);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fixture.EditRepository.SetDescriptionAsync(session.Id, "   ", DateTime.UtcNow));
        }
        finally
        {
            Cleanup(fixture.DbPath);
        }
    }

    private static SceneImageEditSession CreateSession(string sourceImageId = "source") => new()
    {
        SourceImageId = sourceImageId,
        SourceImageSha256 = SourceSha,
        SessionId = "rp-session",
        InteractionId = "interaction",
        Status = SceneImageEditSessionStatus.Active
    };

    private static SceneImageEditCompilationAttempt CreateAttempt(string sessionId, int ordinal) => new()
    {
        EditSessionId = sessionId,
        Ordinal = ordinal,
        RawIntent = "change the shirt to red",
        SourceImageSha256 = SourceSha,
        Status = SceneImageEditCompilationAttemptStatus.Pending,
        ResolvedModelSnapshotJson = "{\"model\":\"qwen-vl\"}",
        CompilerSchemaVersion = "1",
        SystemPromptVersion = "1"
    };

    private static SceneImageEditPromptRevision CreateRevision(string attemptId, int ordinal, string prompt) => new()
    {
        CompilationAttemptId = attemptId,
        Ordinal = ordinal,
        Prompt = prompt,
        RevisionKind = ordinal == 0 ? SceneImageEditPromptRevisionKind.CompilerOutput : SceneImageEditPromptRevisionKind.UserEdited,
        PromptSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)))
    };

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"scene-image-edit-repo-{Guid.NewGuid():N}.db");
        var options = Options.Create(new PersistenceOptions { ConnectionString = $"Data Source={dbPath};Pooling=False" });
        var persistence = new SqlitePersistence(
            options,
            Options.Create(new LmStudioOptions()),
            Options.Create(new StoryAnalysisOptions()),
            Options.Create(new ScenarioAdaptationOptions()),
            NullLogger<SqlitePersistence>.Instance);
        await persistence.InitializeAsync();
        return new Fixture(
            dbPath,
            new ProviderRepository(options, NullLogger<ProviderRepository>.Instance),
            new RegisteredModelRepository(options, NullLogger<RegisteredModelRepository>.Instance),
            new SceneImageRepository(options),
            new SceneImageEditRepository(options));
    }

    private static void Cleanup(string dbPath)
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = dbPath + suffix;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // A parallel Windows test host can retain a transient SQLite handle after disposal.
            }
        }
    }

    private sealed record Fixture(
        string DbPath,
        ProviderRepository ProviderRepository,
        RegisteredModelRepository ModelRepository,
        SceneImageRepository ImageRepository,
        SceneImageEditRepository EditRepository);
}