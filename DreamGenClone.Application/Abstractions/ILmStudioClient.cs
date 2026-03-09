namespace DreamGenClone.Application.Abstractions;

public interface ILmStudioClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}
