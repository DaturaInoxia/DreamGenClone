using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayCommandValidatorTests
{
    private readonly RolePlayCommandValidator _validator =
        new(new BehaviorModeService(NullLogger<BehaviorModeService>.Instance));

    [Fact]
    public void ValidateSubmission_InstructionWithoutIdentity_IsValid()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "s1",
            PromptText = "Direct the next interactions.",
            Intent = PromptIntent.Instruction,
            SubmittedVia = SubmissionSource.PlusButton
        };

        var valid = _validator.ValidateSubmission(submission, out var error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void ValidateSubmission_MessageWithoutIdentity_IsInvalid()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "s1",
            PromptText = "Say something as character.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = ""
        };

        var valid = _validator.ValidateSubmission(submission, out var error);

        Assert.False(valid);
        Assert.Equal("SelectedIdentityId is required for character-scoped intents.", error);
    }

    [Fact]
    public void ValidateContinueRequest_NoParticipantsAndNarrativeDisabled_IsValid()
    {
        var request = new ContinueAsRequest { SessionId = "s1" };

        var valid = _validator.ValidateContinueRequest(request, BehaviorMode.TakeTurns, out var error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }
}
