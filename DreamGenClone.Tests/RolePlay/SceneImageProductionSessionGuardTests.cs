using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Story;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageProductionSessionGuardTests
{
    [Fact]
    public async Task CurrentGeneration_IsAccepted()
    {
        var guard = CreateGuard(new RolePlaySession
        {
            Id = "current",
            SceneImageProductionSchemaGeneration = SceneImageProductionSchema.CurrentGeneration
        });

        await guard.RequireCurrentAsync("current");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MissingOrDifferentGeneration_FailsWithCreateNewSessionGuidance(int? generation)
    {
        var guard = CreateGuard(new RolePlaySession
        {
            Id = "unsupported",
            SceneImageProductionSchemaGeneration = generation
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.RequireCurrentAsync("unsupported"));

        Assert.Contains("Create a new role-play session", error.Message, StringComparison.Ordinal);
        Assert.Contains(SceneImageProductionSchema.CurrentGeneration.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingSession_FailsExplicitly()
    {
        var guard = CreateGuard();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => guard.RequireCurrentAsync("missing"));

        Assert.Contains("was not found", error.Message, StringComparison.Ordinal);
    }

    private static SceneImageProductionSessionGuard CreateGuard(params RolePlaySession[] sessions)
        => new(new StubSessionService(sessions));

    private sealed class StubSessionService(IEnumerable<RolePlaySession> sessions) : ISessionService
    {
        private readonly Dictionary<string, RolePlaySession> _sessions = sessions.ToDictionary(session => session.Id);

        public Task<RolePlaySession?> LoadRolePlaySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult(_sessions.GetValueOrDefault(sessionId));

        public Task SaveRolePlaySessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<StorySession?> LoadStorySessionAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task SaveStorySessionAsync(StorySession session, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SessionListItem>> GetSessionsByTypeAsync(
            string sessionType,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<SessionExportEnvelope?> GetExportEnvelopeAsync(
            string sessionId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}