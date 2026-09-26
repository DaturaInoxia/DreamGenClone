namespace DreamGenClone.Domain.ModelManager;

/// <summary>
/// The ONE owner of "in what order do image models appear, and which one starts selected".
///
/// Every model picker in the app groups by provider and lists the configured default first, so the rule lives here
/// rather than being re-derived per page: a dropdown that sorted itself differently from its neighbour would be a
/// second, silently divergent answer to the same question.
///
/// The order is: the default provider's group first (then providers by name), the default model first inside its
/// group (then models by name). Both "default" facts come from persisted, operator-set row flags
/// (<see cref="Provider.IsDefault"/> / <see cref="RegisteredModel.IsDefault"/>), never from a hardcoded model name
/// or an index into a list.
/// </summary>
public static class ModelChoiceOrdering
{
    /// <summary>Groups key used when a choice carries no provider id (an unregistered provider).</summary>
    private const string UnnamedProviderKey = "\uFFFF";

    /// <summary>
    /// Orders choices for display: default provider's group first, default model first within its group.
    /// Returns a new list; the input is not mutated.
    /// </summary>
    public static IReadOnlyList<SceneImageModelChoice> Order(IEnumerable<SceneImageModelChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        return choices
            .GroupBy(choice => string.IsNullOrWhiteSpace(choice.ProviderId) ? UnnamedProviderKey : choice.ProviderId)
            .OrderBy(group => group.FirstOrDefault(choice => choice.IsDefaultProvider) is null ? 1 : 0)
            .ThenBy(group => group.First().ProviderName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group
                .OrderBy(choice => choice.IsDefaultModel ? 0 : 1)
                .ThenBy(choice => choice.DisplayName, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// The configured default model, or <c>null</c> when the operator has not set one. Honest about absence: a
    /// caller that must report "no default is configured" can, instead of being handed a substitute.
    /// </summary>
    public static SceneImageModelChoice? ConfiguredDefault(IEnumerable<SceneImageModelChoice> orderedChoices)
    {
        ArgumentNullException.ThrowIfNull(orderedChoices);
        return orderedChoices.FirstOrDefault(choice => choice.IsDefaultModel);
    }

    /// <summary>
    /// The model a picker starts on: the operator's configured default when there is one, otherwise the first
    /// model in display order. This is the ONLY place that decision is made, so every picker starts on the same
    /// model and the substitution (default absent) is visible here rather than implied at each call site.
    /// </summary>
    public static SceneImageModelChoice? AutoSelect(IEnumerable<SceneImageModelChoice> orderedChoices)
    {
        ArgumentNullException.ThrowIfNull(orderedChoices);

        var ordered = orderedChoices as IReadOnlyList<SceneImageModelChoice> ?? orderedChoices.ToList();
        return ConfiguredDefault(ordered) ?? ordered.FirstOrDefault();
    }

    /// <summary>
    /// Groups ordered choices by provider for rendering (one group per provider, in <see cref="Order"/> order).
    /// The group label is the provider name.
    /// </summary>
    public static IReadOnlyList<ModelChoiceGroup> Group(IEnumerable<SceneImageModelChoice> orderedChoices)
    {
        ArgumentNullException.ThrowIfNull(orderedChoices);

        return orderedChoices
            .GroupBy(choice => string.IsNullOrWhiteSpace(choice.ProviderId) ? UnnamedProviderKey : choice.ProviderId)
            .Select(group => new ModelChoiceGroup(
                group.First().ProviderName,
                group.First().IsDefaultProvider,
                group.ToList()))
            .ToList();
    }
}

/// <summary>One provider's models, in display order, for rendering as a single picker group.</summary>
public sealed record ModelChoiceGroup(
    string ProviderName,
    bool IsDefaultProvider,
    IReadOnlyList<SceneImageModelChoice> Choices);
