namespace DreamGenClone.Infrastructure.Persistence;

public interface ISqlitePersistence
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // Scenario operations
    Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken cancellationToken = default);
    Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken cancellationToken = default);
    Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteScenarioAsync(string id, CancellationToken cancellationToken = default);
}
