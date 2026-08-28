using DreamGenClone.Domain.PromptTester;

namespace DreamGenClone.Application.PromptTester;

public interface IPromptTestRunRepository
{
    Task SaveAsync(PromptTestRun run, CancellationToken cancellationToken = default);
    Task<List<PromptTestRun>> GetAllAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<PromptTestRun?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
