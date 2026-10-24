namespace DreamGenClone.Web.Application.RolePlay.ImageStep;

/// <summary>
/// How one prompt element reaches the image: restated deterministically by the POV projection, emitted by the
/// canonical appearance side-channel, or simply read as text by the compile LLM inside the brief JSON.
/// </summary>
/// <remarks>
/// The inspector labels a row with this so the operator knows whether editing it is a guarantee ("this sentence in
/// the SHOT block changes") or a steer ("the compiler reads this and weighs it"). Both are real; the difference is
/// what "the effect on the image" means for each row.
/// </remarks>
public enum PromptElementConsumer
{
    /// <summary>The value is restated verbatim by the POV projection / camera line (position, action, sightline, clothing, location).</summary>
    DeterministicFrame = 1,

    /// <summary>The value is emitted in the DEPICTED CHARACTER APPEARANCE block through the appearance side-channel.</summary>
    DeterministicAppearance = 2,

    /// <summary>The value is read by the compile LLM as part of the brief JSON.</summary>
    CompilerRead = 3
}

/// <summary>
/// One editor concept: a set of payload element keys the operator edits or removes TOGETHER, so a fix that should
/// touch every leaf carrying that fact does not have to be discovered leaf by leaf.
/// </summary>
/// <param name="Key">Stable concept id (e.g. <c>lighting</c>).</param>
/// <param name="Label">Operator-facing name.</param>
/// <param name="Consumer">How the element reaches the image.</param>
/// <param name="EditScopes">The payload keys written when the operator edits the row.</param>
/// <param name="RemoveScopes">
/// The payload keys removed when the operator ticks "remove". Null means the same set as <see cref="EditScopes"/>.
/// It is a SEPARATE set from the edit scopes because "remove the room" has to mean the same 18 elements a bound
/// location reference removes, while "edit the location text" only rewrites the location sentences.
/// </param>
/// <param name="ReadPath">
/// The snapshot path the row's ORIGINAL value is read from when it differs from <see cref="EditScopes"/>'s first key
/// (<c>scene.objects</c> is the applier's array-aware write key; the value actually lives at <c>frozenState.objects</c>).
/// </param>
public sealed record PromptElementCluster(
    string Key,
    string Label,
    PromptElementConsumer Consumer,
    IReadOnlyList<string> EditScopes,
    IReadOnlyList<string>? RemoveScopes = null,
    string? ReadPath = null)
{
    /// <summary>The keys a remove tick removes — <see cref="RemoveScopes"/> when declared, else the edit scopes.</summary>
    public IReadOnlyList<string> EffectiveRemoveScopes => RemoveScopes ?? EditScopes;
}

/// <summary>
/// The ONE answer to "which payload elements does this editor concept cover?" (B-166 extension).
/// </summary>
/// <remarks>
/// The compiled brief repeats a single fact across many leaves — "shaded" lives in the lighting sentence, the opening
/// lighting, the closing lighting, the visual description and the continuity summaries. Editing one leaf and not its
/// siblings is how an operator's lighting fix survives in only one of the five places it was stated, and why they had
/// to hunt the spots down. This table groups the leaves so the inspector edits and removes the WHOLE concept at once,
/// and shows the leaves it touched so the coverage is visible rather than discovered.
/// </remarks>
public static class PromptElementClusters
{
    /// <summary>The scene-level concepts, in display order.</summary>
    public static readonly IReadOnlyList<PromptElementCluster> Scene =
    [
        new("location", "Location", PromptElementConsumer.DeterministicFrame,
            ["frozenState.location", "continuity.start.location", "continuity.end.location"],
            StepPromptElementPlan.LocationScopes),
        new("environment", "Environment", PromptElementConsumer.CompilerRead,
            ["frozenState.environment"]),
        new("timeOfDay", "Time of day", PromptElementConsumer.CompilerRead,
            ["frozenState.timeOfDay"]),
        new("lighting", "Lighting", PromptElementConsumer.CompilerRead,
            ["frozenState.lighting", "continuity.start.lighting", "continuity.end.lighting"]),
        new("mood", "Mood", PromptElementConsumer.CompilerRead,
            ["frozenState.mood"]),
        new("objects", "Objects / props", PromptElementConsumer.CompilerRead,
            ["scene.objects"], ReadPath: "frozenState.objects"),
        new("visualDescription", "Visual description (whole event)", PromptElementConsumer.CompilerRead,
            ["frozenState.visualDescription"]),
        new("momentProse", "Moment prose (enrichment)", PromptElementConsumer.CompilerRead,
            ["moment.frozenState"]),
        new("visibleAction", "Visible action", PromptElementConsumer.CompilerRead,
            ["moment.visibleAction"]),
        new("continuityState", "Continuity (global)", PromptElementConsumer.CompilerRead,
            ["frozenState.continuityState"])
    ];

    /// <summary>The per-character concepts, in display order, each expanded with the character's payload key.</summary>
    public static readonly IReadOnlyList<PromptElementCluster> Character =
    [
        new("physicalLocation", "Physical location", PromptElementConsumer.DeterministicFrame,
            ["character:{key}.physicalLocation"]),
        new("position", "Position", PromptElementConsumer.DeterministicFrame,
            ["character:{key}.position"]),
        new("actionOrObservation", "Action / observation", PromptElementConsumer.DeterministicFrame,
            ["character:{key}.actionOrObservation"]),
        new("sightline", "Sightline", PromptElementConsumer.DeterministicFrame,
            ["character:{key}.sightline"]),
        new("clothing", "Wardrobe", PromptElementConsumer.DeterministicFrame,
            ["character:{key}.clothing"]),
        new("appearance", "Appearance (auto)", PromptElementConsumer.DeterministicAppearance,
            ["character:{key}.appearance"])
    ];

    private const string CharacterPlaceholder = "{key}";

    /// <summary>The scene-level cluster with the given key, or null.</summary>
    public static PromptElementCluster? SceneCluster(string key)
        => Scene.FirstOrDefault(cluster => string.Equals(cluster.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>The per-character cluster with the given key, or null.</summary>
    public static PromptElementCluster? CharacterCluster(string key)
        => Character.FirstOrDefault(cluster => string.Equals(cluster.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A per-character cluster's scopes with <c>{key}</c> expanded to the given character payload key.
    /// </summary>
    public static IReadOnlyList<string> ScopesFor(PromptElementCluster cluster, string characterKey)
        => cluster.EditScopes
            .Select(scope => scope.Replace(CharacterPlaceholder, characterKey, StringComparison.Ordinal))
            .ToList();
}
