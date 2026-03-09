namespace DreamGenClone.Infrastructure.Persistence;

public interface ISqlitePersistence
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
