namespace DreamGenClone.Web.Application.RolePlay;

/// <summary>
/// Common interface for all behavioral prompt injectors in the <see cref="SceneDirectionCoordinator"/> pipeline.
/// Each injector contributes a self-contained text block to the prompt, positioned by priority.
/// </summary>
public interface IPromptInjector
{
    /// <summary>Unique identifier for this injector (kebab-case, e.g., "turn-context", "time-location").</summary>
    string Id { get; }

    /// <summary>Assembly order priority — lower values fire earlier. Gaps (e.g., 5, 10, 20) allow future insertion.</summary>
    int Priority { get; }

    /// <summary>
    /// Determines whether this injector should emit text for the given context.
    /// Pure predicate — idempotent for identical context, no side effects.
    /// </summary>
    bool ShouldFire(PromptInjectionContext context);

    /// <summary>
    /// Builds the prompt text block for this injector. MUST NOT throw for a context
    /// where <see cref="ShouldFire"/> returned true. Exceptions propagate per
    /// the fail-fast contract (FR-015).
    /// Result MUST NOT contain leading/trailing newlines — coordinator handles spacing.
    /// </summary>
    string BuildText(PromptInjectionContext context);
}
