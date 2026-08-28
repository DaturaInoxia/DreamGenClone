namespace DreamGenClone.Domain.RolePlay;

/// <summary>
/// Prompt attention zone for the 17-slot architecture.
/// A = Primacy (scene grounding, never trimmed).
/// B = Context (world + history, trimmable per priority).
/// C = Recency (directives + instruction, never trimmed except where noted).
/// </summary>
public enum PromptZone
{
    A = 0,
    B = 1,
    C = 2
}
