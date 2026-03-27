using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public interface IRolePlayCommandValidator
{
    bool ValidateSubmission(UnifiedPromptSubmission submission, out string validationError);

    bool ValidateContinueRequest(ContinueAsRequest request, BehaviorMode mode, out string validationError);
}
