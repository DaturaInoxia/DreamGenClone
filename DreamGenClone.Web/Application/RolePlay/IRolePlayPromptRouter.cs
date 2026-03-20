using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed record PromptCommandRoute(
    PromptIntent Intent,
    string TargetCommand,
    bool RequiresInstructionPayload,
    bool RequiresActorContext);

public interface IRolePlayPromptRouter
{
    PromptCommandRoute Resolve(PromptIntent intent);

    bool TryResolve(PromptIntent intent, out PromptCommandRoute route);
}
