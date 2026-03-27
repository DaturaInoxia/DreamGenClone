using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayUnifiedPromptValidationTests
{
    [Fact]
    public void IsValid_WhenSubmissionIsComplete_ReturnsTrue()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            PromptText = "Continue the scene.",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            BehaviorModeAtSubmit = BehaviorMode.TakeTurns
        };

        var result = submission.IsValid(out var validationError);

        Assert.True(result);
        Assert.Equal(string.Empty, validationError);
    }

    [Theory]
    [InlineData("", "prompt", "persona:you", "SessionId is required.")]
    [InlineData("s1", "", "persona:you", "PromptText is required.")]
    [InlineData("s1", "prompt", "", "SelectedIdentityId is required for character-scoped intents.")]
    public void IsValid_WhenRequiredFieldMissing_ReturnsFalse(
        string sessionId,
        string promptText,
        string selectedIdentityId,
        string expectedError)
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = sessionId,
            PromptText = promptText,
            SelectedIdentityId = selectedIdentityId,
            SelectedIdentityType = IdentityOptionSource.Persona
        };

        var result = submission.IsValid(out var validationError);

        Assert.False(result);
        Assert.Equal(expectedError, validationError);
    }

    [Fact]
    public void IsValid_WhenCustomIdentityWithoutName_ReturnsFalse()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            PromptText = "Follow this direction",
            Intent = PromptIntent.Message,
            SelectedIdentityId = "custom",
            SelectedIdentityType = IdentityOptionSource.CustomCharacter,
            CustomIdentityName = " "
        };

        var result = submission.IsValid(out var validationError);

        Assert.False(result);
        Assert.Equal("Custom identity requires a custom name.", validationError);
    }

    [Fact]
    public void IsValid_WhenInstructionWithoutIdentity_ReturnsTrue()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            PromptText = "Push the story tension higher.",
            Intent = PromptIntent.Instruction,
            SubmittedVia = SubmissionSource.PlusButton
        };

        var result = submission.IsValid(out var validationError);

        Assert.True(result);
        Assert.Equal(string.Empty, validationError);
    }

    [Theory]
    [InlineData(PromptIntent.Message)]
    [InlineData(PromptIntent.Narrative)]
    public void IsValid_WhenCharacterIntentWithoutIdentity_ReturnsFalse(PromptIntent intent)
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            PromptText = "Use this tone and direction.",
            Intent = intent,
            SelectedIdentityId = string.Empty,
            SelectedIdentityType = IdentityOptionSource.Persona
        };

        var result = submission.IsValid(out var validationError);

        Assert.False(result);
        Assert.Equal("SelectedIdentityId is required for character-scoped intents.", validationError);
    }
}
