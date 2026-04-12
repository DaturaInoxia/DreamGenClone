using DreamGenClone.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayPendingDecisionPrompt
{
    public required DecisionPoint DecisionPoint { get; init; }
    public required IReadOnlyList<DecisionOption> Options { get; init; }
}
