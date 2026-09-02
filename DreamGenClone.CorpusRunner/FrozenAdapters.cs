using DreamGenClone.Application.Processing;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.Processing;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.CorpusRunner;

internal sealed class FrozenSessionReader(RolePlaySession session) : ISceneBeatSessionReader
{
    public Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<RolePlaySession?>(session.Id == sessionId ? session : null);
}

internal sealed class FrozenTurnReader(RolePlayTurn turn) : IRolePlayTurnReader
{
    public Task<RolePlayTurn?> GetTurnAsync(string sessionId, string turnId, CancellationToken cancellationToken = default)
        => Task.FromResult<RolePlayTurn?>(turn.SessionId == sessionId && turn.TurnId == turnId ? turn : null);
}

internal sealed class FrozenScenarioReader(string scenarioId, IReadOnlyList<Character> characters) : ISceneBeatScenarioReader
{
    public Task<IReadOnlyList<Character>?> GetCharactersAsync(string requestedScenarioId)
        => Task.FromResult<IReadOnlyList<Character>?>(requestedScenarioId == scenarioId ? characters : null);
}

internal sealed class RecordingDurableQueue : IDurableBackgroundJobQueue
{
    private readonly List<DurableBackgroundJob> _jobs = [];

    public DurableBackgroundJob TakeLast(string jobType)
    {
        var job = _jobs.Last(item => item.JobType == jobType);
        job.AttemptCount = job.MaxAttempts;
        return job;
    }

    public Task<bool> TryEnqueueAsync(DurableBackgroundJob job, CancellationToken cancellationToken = default)
    {
        _jobs.Add(job);
        return Task.FromResult(true);
    }

    public Task<DurableBackgroundJob?> GetAsync(string jobId, CancellationToken cancellationToken = default)
        => Task.FromResult(_jobs.SingleOrDefault(item => item.Id == jobId));

    public Task<bool> TryCancelAsync(string jobId, DateTime cancelledUtc, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task WaitForWorkAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}