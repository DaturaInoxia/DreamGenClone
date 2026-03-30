namespace DreamGenClone.Application.Abstractions;

public interface ILmStudioClient
{
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default);

    Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a completion with custom model settings.
    /// </summary>
    Task<string> GenerateAsync(
        string prompt,
        string model,
        double temperature,
        double topP,
        int maxTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a completion with a separate system message and custom model settings.
    /// </summary>
    Task<string> GenerateAsync(
        string systemMessage,
        string userMessage,
        string model,
        double temperature,
        double topP,
        int maxTokens,
        CancellationToken cancellationToken = default);
}
