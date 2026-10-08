using DreamGenClone.Application.Processing;

namespace DreamGenClone.Application.RolePlay;

/// <summary>
/// Resolves the video lane's own queue bounds from the <c>RolePlaySceneVideo</c> function default (B-152, D-7).
/// Kept separate from <see cref="ISceneBeatAnalyzerResolver"/> so the video lane can never inherit the analyzer's
/// concurrency (3 parallel clips on one 16 GB GPU is an OOM, not a speed-up) or its poll interval.
/// </summary>
public interface ISceneVideoLaneResolver
{
    /// <summary>
    /// The video lane's bounds. Fails fast with an explicit diagnostic when the function default is missing or its
    /// queue settings are outside their configured bounds.
    /// </summary>
    Task<DurableLaneSettings> ResolveAsync(CancellationToken cancellationToken = default);
}
