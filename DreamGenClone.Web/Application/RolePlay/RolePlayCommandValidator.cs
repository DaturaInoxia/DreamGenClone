using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayCommandValidator : IRolePlayCommandValidator
{
    private readonly IBehaviorModeService _behaviorModeService;

    public RolePlayCommandValidator(IBehaviorModeService behaviorModeService)
    {
        _behaviorModeService = behaviorModeService;
    }

    public bool ValidateSubmission(UnifiedPromptSubmission submission, out string validationError)
    {
        if (!submission.IsValid(out validationError))
        {
            return false;
        }

        if (submission.Intent is PromptIntent.Message or PromptIntent.Narrative
            && string.IsNullOrWhiteSpace(submission.SelectedIdentityId))
        {
            validationError = "SelectedIdentityId is required for character-scoped intents.";
            return false;
        }

        validationError = string.Empty;
        return true;
    }

    public bool ValidateContinueRequest(ContinueAsRequest request, BehaviorMode mode, out string validationError)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            validationError = "SessionId is required.";
            return false;
        }

        if (request.IsClearAction)
        {
            validationError = string.Empty;
            return true;
        }

        var orderedParticipants = ContinueAsOrdering.OrderDistinct(request.SelectedParticipants);

        foreach (var actor in orderedParticipants)
        {
            if (!_behaviorModeService.IsContinuationAllowed(mode, actor))
            {
                validationError = $"Actor '{actor}' is not allowed in mode '{mode}'.";
                return false;
            }
        }

        validationError = string.Empty;
        return true;
    }
}
