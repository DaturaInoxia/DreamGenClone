using DreamGenClone.Application.StoryAnalysis.Models;

namespace DreamGenClone.Application.StoryAnalysis.Abstractions;

/// <summary>
/// Generates LLM behavioral frame text for each character in a session that has an encounter profile bound.
/// Behavioral frames are injected as HARD CONSTRAINTs into the continuation prompt.
/// </summary>
public interface IBehavioralFrameGenerator
{
    /// <summary>
    /// Generates behavioral frame text for all characters with bound encounter profiles.
    /// Returns a dictionary keyed by character display label (e.g., "Sarah (Wife)").
    /// Characters without a bound profile are omitted from the result (no empty entries).
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GenerateFramesAsync(
        IReadOnlyDictionary<string, string> characterEncounterProfileIds,
        IReadOnlyList<ScenarioCharacter> characters,
        CancellationToken cancellationToken = default);
}
