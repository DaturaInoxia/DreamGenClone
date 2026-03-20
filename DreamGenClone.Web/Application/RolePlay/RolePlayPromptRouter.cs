using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayPromptRouter : IRolePlayPromptRouter
{
    private static readonly IReadOnlyDictionary<PromptIntent, PromptCommandRoute> Routes =
        new Dictionary<PromptIntent, PromptCommandRoute>
        {
            [PromptIntent.Message] = new(PromptIntent.Message, "continue-message", false, true),
            [PromptIntent.Narrative] = new(PromptIntent.Narrative, "continue-narrative", false, true),
            [PromptIntent.Instruction] = new(PromptIntent.Instruction, "append-instruction", true, false)
        };

    public PromptCommandRoute Resolve(PromptIntent intent)
    {
        if (!Routes.TryGetValue(intent, out var route))
        {
            throw new InvalidOperationException($"No route configured for intent '{intent}'.");
        }

        return route;
    }

    public bool TryResolve(PromptIntent intent, out PromptCommandRoute route)
    {
        return Routes.TryGetValue(intent, out route!);
    }
}
