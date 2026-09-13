using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// The ONE media edit store (B-124 B124-012). These tests pin the union behaviour of the two
/// stores it replaced, and pin the one-time migration of their rows.
/// </summary>
public sealed class MediaEditRepositoryTests
{
    private const string Sha = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Fact]
    public async Task Session_RoundTripsEveryFieldIncludingSubjectScope()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        session.SubjectScopeId = "session-1";
        await fixture.Repository.CreateSessionAsync(session);

        var loaded = await fixture.Repository.GetSessionAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.Equal(MediaEditSubjectKind.SceneImage, loaded!.SubjectKind);
        Assert.Equal("interaction-1", loaded.SubjectId);
        Assert.Equal("session-1", loaded.SubjectScopeId);
        Assert.Equal("source-1", loaded.SourceImageId);
        Assert.Equal(Sha, loaded.SourceImageSha256);
        Assert.Equal(MediaEditSessionStatus.Active, loaded.Status);
        Assert.Null(loaded.DescriptionText);
    }

    [Fact]
    public async Task Session_RequiresExplicitSubjectKindAndStatus()
    {
        using var fixture = await Fixture.CreateAsync();

        var noKind = NewSession();
        noKind.SubjectKind = MediaEditSubjectKind.Unknown;
        var kindError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.CreateSessionAsync(noKind));
        Assert.Contains("subject kind", kindError.Message, StringComparison.OrdinalIgnoreCase);

        var noStatus = NewSession();
        noStatus.Status = MediaEditSessionStatus.Unknown;
        var statusError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.CreateSessionAsync(noStatus));
        Assert.Contains("status", statusError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_RequiresAWellFormedChecksum()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        session.SourceImageSha256 = "not-a-hash";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.CreateSessionAsync(session));

        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SessionStatus_FollowsTheSharedTransitionRules()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);

        await fixture.Repository.UpdateSessionStatusAsync(session.Id, MediaEditSessionStatus.Ready, DateTime.UtcNow);

        var illegal = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository
            .UpdateSessionStatusAsync(session.Id, MediaEditSessionStatus.ClarificationRequired, DateTime.UtcNow));
        Assert.Contains("cannot transition", illegal.Message, StringComparison.Ordinal);

        var completedWithoutTimestamp = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository
            .UpdateSessionStatusAsync(session.Id, MediaEditSessionStatus.Completed, DateTime.UtcNow));
        Assert.Contains("completion timestamp", completedWithoutTimestamp.Message, StringComparison.Ordinal);

        await fixture.Repository.UpdateSessionStatusAsync(
            session.Id, MediaEditSessionStatus.Completed, DateTime.UtcNow, DateTime.UtcNow);
        var loaded = await fixture.Repository.GetSessionAsync(session.Id);
        Assert.Equal(MediaEditSessionStatus.Completed, loaded!.Status);
    }

    [Fact]
    public async Task Session_IsQueryableBySubjectAndSource()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);

        var found = await fixture.Repository.GetLatestSessionAsync(
            MediaEditSubjectKind.SceneImage, "interaction-1", "source-1");
        var otherSubject = await fixture.Repository.GetLatestSessionAsync(
            MediaEditSubjectKind.AssetImage, "interaction-1", "source-1");

        Assert.Equal(session.Id, found!.Id);
        Assert.Null(otherSubject);
    }

    [Fact]
    public async Task Attempt_MustStartPendingAndFollowsAttemptTransitions()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);

        var notPending = NewAttempt(session.Id, ordinal: 0);
        notPending.Status = SceneImageEditCompilationAttemptStatus.Ready;
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.CreateAttemptAsync(notPending));
        Assert.Contains("must be Pending", error.Message, StringComparison.Ordinal);

        var attempt = NewAttempt(session.Id, ordinal: 0);
        await fixture.Repository.CreateAttemptAsync(attempt);

        var illegal = NewAttempt(session.Id, ordinal: 0);
        illegal.Id = attempt.Id;
        illegal.Status = SceneImageEditCompilationAttemptStatus.Ready;
        var transitionError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.UpdateAttemptAsync(illegal));
        Assert.Contains("cannot transition", transitionError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Attempt_OrdinalIsUniquePerSession()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);
        await fixture.Repository.CreateAttemptAsync(NewAttempt(session.Id, ordinal: 0));

        await Assert.ThrowsAsync<SqliteException>(
            () => fixture.Repository.CreateAttemptAsync(NewAttempt(session.Id, ordinal: 0)));
    }

    [Fact]
    public async Task AttemptLifecycle_PersistsTheCompilerOutcome()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);
        var attempt = NewAttempt(session.Id, ordinal: 0);
        await fixture.Repository.CreateAttemptAsync(attempt);

        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        attempt.StartedUtc = DateTime.UtcNow;
        await fixture.Repository.UpdateAttemptAsync(attempt);

        attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
        attempt.RawModelResponse = "{\"status\":\"ready\"}";
        attempt.ParsedResultJson = "{\"status\":1}";
        attempt.CompletedUtc = DateTime.UtcNow;
        await fixture.Repository.UpdateAttemptAsync(attempt);

        var latest = await fixture.Repository.GetLatestAttemptAsync(session.Id);
        Assert.Equal(attempt.Id, latest!.Id);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, latest.Status);
        Assert.Equal("{\"status\":\"ready\"}", latest.RawModelResponse);
    }

    [Fact]
    public async Task Revision_RequiresAnExplicitKindAndUniqueOrdinalAndPrompt()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);
        var attempt = NewAttempt(session.Id, ordinal: 0);
        await fixture.Repository.CreateAttemptAsync(attempt);

        var noKind = NewRevision(attempt.Id, ordinal: 0, prompt: "prompt-a");
        noKind.RevisionKind = SceneImageEditPromptRevisionKind.Unknown;
        var kindError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Repository.CreateRevisionAsync(noKind));
        Assert.Contains("kind", kindError.Message, StringComparison.OrdinalIgnoreCase);

        await fixture.Repository.CreateRevisionAsync(NewRevision(attempt.Id, ordinal: 0, prompt: "prompt-a"));

        await Assert.ThrowsAsync<SqliteException>(
            () => fixture.Repository.CreateRevisionAsync(NewRevision(attempt.Id, ordinal: 0, prompt: "prompt-b")));
        await Assert.ThrowsAsync<SqliteException>(
            () => fixture.Repository.CreateRevisionAsync(NewRevision(attempt.Id, ordinal: 1, prompt: "prompt-a")));

        var revisions = await fixture.Repository.ListRevisionsAsync(attempt.Id);
        Assert.Single(revisions);
    }

    [Fact]
    public async Task ExecutableRevision_RefusesStaleProvenance()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);
        var attempt = NewAttempt(session.Id, ordinal: 0);
        await fixture.Repository.CreateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Compiling;
        await fixture.Repository.UpdateAttemptAsync(attempt);
        attempt.Status = SceneImageEditCompilationAttemptStatus.Ready;
        await fixture.Repository.UpdateAttemptAsync(attempt);
        var revision = NewRevision(attempt.Id, ordinal: 0, prompt: "prompt-a");
        await fixture.Repository.CreateRevisionAsync(revision);

        var resolved = await fixture.Repository.GetExecutableRevisionAsync(
            session.Id, attempt.Id, revision.Id, Sha, revision.PromptSha256);
        Assert.Equal(revision.Id, resolved.Id);

        var staleSource = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository
            .GetExecutableRevisionAsync(session.Id, attempt.Id, revision.Id, new string('A', 64), revision.PromptSha256));
        Assert.Contains("provenance", staleSource.Message, StringComparison.OrdinalIgnoreCase);

        var stalePrompt = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Repository
            .GetExecutableRevisionAsync(session.Id, attempt.Id, revision.Id, Sha, new string('B', 64)));
        Assert.Contains("provenance", stalePrompt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteSession_RemovesItsAttemptsAndRevisions()
    {
        using var fixture = await Fixture.CreateAsync();
        var session = NewSession();
        await fixture.Repository.CreateSessionAsync(session);
        var attempt = NewAttempt(session.Id, ordinal: 0);
        await fixture.Repository.CreateAttemptAsync(attempt);
        var revision = NewRevision(attempt.Id, ordinal: 0, prompt: "prompt-a");
        await fixture.Repository.CreateRevisionAsync(revision);

        await fixture.Repository.DeleteSessionAsync(session.Id);

        Assert.Null(await fixture.Repository.GetSessionAsync(session.Id));
        Assert.Null(await fixture.Repository.GetAttemptAsync(attempt.Id));
        Assert.Null(await fixture.Repository.GetRevisionAsync(revision.Id));
    }

    [Fact]
    public async Task Backfill_CopiesBothLegacyStoresPreservingIdsAndSubject()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.CreateLegacyRowsAsync();

        var report = await fixture.Repository.EnsureSchemaAsync();

        Assert.Equal(2, report.Sessions);
        Assert.Equal(2, report.Attempts);
        Assert.Equal(2, report.Revisions);

        var scene = await fixture.Repository.GetSessionAsync("legacy-scene-session");
        Assert.Equal(MediaEditSubjectKind.SceneImage, scene!.SubjectKind);
        Assert.Equal("legacy-interaction", scene.SubjectId);
        Assert.Equal("legacy-session", scene.SubjectScopeId);
        Assert.Equal("legacy-scene-description", scene.DescriptionText);
        Assert.Equal(MediaEditSessionStatus.Ready, scene.Status);

        var asset = await fixture.Repository.GetSessionAsync("legacy-asset-session");
        Assert.Equal(MediaEditSubjectKind.AssetImage, asset!.SubjectKind);
        Assert.Equal("legacy-asset", asset.SubjectId);
        Assert.Null(asset.SubjectScopeId);
        Assert.Equal("legacy-asset-description", asset.DescriptionText);

        var attempt = await fixture.Repository.GetAttemptAsync("legacy-scene-attempt");
        Assert.Equal("legacy-scene-session", attempt!.EditSessionId);
        Assert.Equal(SceneImageEditCompilationAttemptStatus.Ready, attempt.Status);
        Assert.Equal("{\"legacy\":true}", attempt.ParsedResultJson);

        var revision = await fixture.Repository.GetRevisionAsync("legacy-asset-revision");
        Assert.Equal("legacy-asset-attempt", revision!.CompilationAttemptId);
        Assert.Equal("legacy asset prompt", revision.Prompt);
    }

    [Fact]
    public async Task Backfill_IsIdempotentAndCanBeRerunForTheCutover()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.CreateLegacyRowsAsync();

        var first = await fixture.Repository.EnsureSchemaAsync();
        var second = await fixture.Repository.EnsureSchemaAsync();
        var rerun = await fixture.Repository.ReBackfillLegacyAsync();

        Assert.Equal(6, first.Total);
        Assert.Equal(0, second.Total);
        Assert.Equal(0, rerun.Total);
    }

    [Fact]
    public async Task Backfill_PicksUpLegacyRowsCreatedAfterTheMarker()
    {
        using var fixture = await Fixture.CreateAsync();
        var initial = await fixture.Repository.EnsureSchemaAsync();
        Assert.Equal(0, initial.Total);

        await fixture.CreateLegacyRowsAsync();
        var cutover = await fixture.Repository.ReBackfillLegacyAsync();

        Assert.Equal(6, cutover.Total);
        Assert.NotNull(await fixture.Repository.GetSessionAsync("legacy-scene-session"));
        Assert.NotNull(await fixture.Repository.GetSessionAsync("legacy-asset-session"));
    }

    private static MediaEditSession NewSession() => new()
    {
        Id = $"session-{Guid.NewGuid():N}",
        SubjectKind = MediaEditSubjectKind.SceneImage,
        SubjectId = "interaction-1",
        SourceImageId = "source-1",
        SourceImageSha256 = Sha,
        Status = MediaEditSessionStatus.Active
    };

    private static MediaEditCompilationAttempt NewAttempt(string sessionId, int ordinal) => new()
    {
        Id = $"attempt-{Guid.NewGuid():N}",
        EditSessionId = sessionId,
        Ordinal = ordinal,
        RawIntent = "make it red",
        SourceImageSha256 = Sha,
        Status = SceneImageEditCompilationAttemptStatus.Pending,
        ResolvedModelSnapshotJson = "{\"model\":\"qwen\"}",
        CompilerSchemaVersion = "schema-1",
        SystemPromptVersion = "prompt-1"
    };

    private static MediaEditPromptRevision NewRevision(string attemptId, int ordinal, string prompt) => new()
    {
        Id = $"revision-{Guid.NewGuid():N}",
        CompilationAttemptId = attemptId,
        Ordinal = ordinal,
        Prompt = prompt,
        RevisionKind = SceneImageEditPromptRevisionKind.CompilerOutput,
        PromptSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(prompt)))
    };

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, string connectionString, MediaEditRepository repository)
        {
            Root = root;
            ConnectionString = connectionString;
            Repository = repository;
        }

        public string Root { get; }

        public string ConnectionString { get; }

        public MediaEditRepository Repository { get; }

        public static Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"media-edit-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var dbPath = Path.Combine(root, "media-edit.db");
            var connectionString = $"Data Source={dbPath};Pooling=False";
            var options = Options.Create(new PersistenceOptions { ConnectionString = connectionString });
            return Task.FromResult(new Fixture(root, connectionString, new MediaEditRepository(options)));
        }

        /// <summary>
        /// Creates the two legacy stores exactly as they shipped, with one session/attempt/revision
        /// pair in each. This intentionally pins the legacy shape the migration must read.
        /// </summary>
        public async Task CreateLegacyRowsAsync()
        {
            await using var connection = new SqliteConnection(ConnectionString);
            await connection.OpenAsync();
            await using var create = connection.CreateCommand();
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS SceneImageEditSessions (
                    Id TEXT PRIMARY KEY, SourceImageId TEXT NOT NULL, SourceImageSha256 TEXT NOT NULL,
                    SessionId TEXT NOT NULL, InteractionId TEXT NOT NULL, Status TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, CompletedUtc TEXT NULL, DescriptionText TEXT NULL);
                CREATE TABLE IF NOT EXISTS SceneImageEditCompilationAttempts (
                    Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL, RawIntent TEXT NOT NULL,
                    ClarificationContextJson TEXT NULL, SourceImageSha256 TEXT NOT NULL, Status TEXT NOT NULL,
                    ResolvedModelSnapshotJson TEXT NOT NULL, CompilerSchemaVersion TEXT NOT NULL, SystemPromptVersion TEXT NOT NULL,
                    RawModelResponse TEXT NULL, ParsedResultJson TEXT NULL, Error TEXT NULL, CreatedUtc TEXT NOT NULL,
                    StartedUtc TEXT NULL, CompletedUtc TEXT NULL);
                CREATE TABLE IF NOT EXISTS SceneImageEditPromptRevisions (
                    Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL, Prompt TEXT NOT NULL,
                    RevisionKind TEXT NOT NULL, PromptSha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditSessions (
                    Id TEXT PRIMARY KEY, AssetId TEXT NOT NULL, SourceImageId TEXT NOT NULL, SourceImageSha256 TEXT NOT NULL,
                    Status TEXT NOT NULL, DescriptionText TEXT NULL, CreatedUtc TEXT NOT NULL, UpdatedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NULL);
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditCompilationAttempts (
                    Id TEXT PRIMARY KEY, EditSessionId TEXT NOT NULL, Ordinal INTEGER NOT NULL, RawIntent TEXT NOT NULL,
                    ClarificationContextJson TEXT NULL, SourceImageSha256 TEXT NOT NULL, Status TEXT NOT NULL,
                    ResolvedModelSnapshotJson TEXT NOT NULL, CompilerSchemaVersion TEXT NOT NULL, SystemPromptVersion TEXT NOT NULL,
                    RawModelResponse TEXT NULL, ParsedResultJson TEXT NULL, Error TEXT NULL, CreatedUtc TEXT NOT NULL,
                    StartedUtc TEXT NULL, CompletedUtc TEXT NULL);
                CREATE TABLE IF NOT EXISTS SceneAssetImageEditPromptRevisions (
                    Id TEXT PRIMARY KEY, CompilationAttemptId TEXT NOT NULL, Ordinal INTEGER NOT NULL, Prompt TEXT NOT NULL,
                    RevisionKind TEXT NOT NULL, PromptSha256 TEXT NOT NULL, CreatedUtc TEXT NOT NULL);
                """;
            await create.ExecuteNonQueryAsync();

            await using var rows = connection.CreateCommand();
            rows.CommandText = """
                INSERT OR IGNORE INTO SceneImageEditSessions
                    (Id, SourceImageId, SourceImageSha256, SessionId, InteractionId, Status, CreatedUtc, UpdatedUtc, CompletedUtc, DescriptionText)
                VALUES
                    ('legacy-scene-session','legacy-source','0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF',
                     'legacy-session','legacy-interaction','Ready',
                     '2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z',NULL,'legacy-scene-description');
                INSERT OR IGNORE INTO SceneAssetImageEditSessions
                    (Id, AssetId, SourceImageId, SourceImageSha256, Status, DescriptionText, CreatedUtc, UpdatedUtc, CompletedUtc)
                VALUES
                    ('legacy-asset-session','legacy-asset','legacy-asset-source','0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF',
                     'Ready','legacy-asset-description','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z',NULL);

                INSERT OR IGNORE INTO SceneImageEditCompilationAttempts
                    (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                     ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                     ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc)
                VALUES
                    ('legacy-scene-attempt','legacy-scene-session',0,'intent',NULL,
                     '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF','Ready',
                     '{"model":"qwen"}','schema-1','prompt-1','resp','{"legacy":true}',NULL,
                     '2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z','2026-09-01T00:00:00.0000000Z');
                INSERT OR IGNORE INTO SceneAssetImageEditCompilationAttempts
                    (Id, EditSessionId, Ordinal, RawIntent, ClarificationContextJson, SourceImageSha256, Status,
                     ResolvedModelSnapshotJson, CompilerSchemaVersion, SystemPromptVersion, RawModelResponse,
                     ParsedResultJson, Error, CreatedUtc, StartedUtc, CompletedUtc)
                VALUES
                    ('legacy-asset-attempt','legacy-asset-session',0,'intent',NULL,
                     '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF','Ready',
                     '{"model":"qwen"}','schema-1','prompt-1',NULL,NULL,NULL,
                     '2026-09-01T00:00:00.0000000Z',NULL,NULL);

                INSERT OR IGNORE INTO SceneImageEditPromptRevisions
                    (Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc)
                VALUES
                    ('legacy-scene-revision','legacy-scene-attempt',0,'legacy scene prompt','CompilerOutput',
                     '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF','2026-09-01T00:00:00.0000000Z');
                INSERT OR IGNORE INTO SceneAssetImageEditPromptRevisions
                    (Id, CompilationAttemptId, Ordinal, Prompt, RevisionKind, PromptSha256, CreatedUtc)
                VALUES
                    ('legacy-asset-revision','legacy-asset-attempt',0,'legacy asset prompt','CompilerOutput',
                     '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF','2026-09-01T00:00:00.0000000Z');
                """;
            await rows.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            // No SqliteConnection.ClearAllPools() here: it is process-wide and would destabilise
            // other test classes running in parallel. These connections use Pooling=False.
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of the temp fixture.
            }
        }
    }
}
