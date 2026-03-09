namespace DreamGenClone.Application.Sessions;

public interface IAutoSaveCoordinator
{
    void RequestSave(string reason, Func<CancellationToken, Task> saveAction);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
