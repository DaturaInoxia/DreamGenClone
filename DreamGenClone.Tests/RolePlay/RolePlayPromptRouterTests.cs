using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayPromptRouterTests
{
    [Theory]
    [InlineData(PromptIntent.Message, "continue-message", false, true)]
    [InlineData(PromptIntent.Narrative, "continue-narrative", false, true)]
    [InlineData(PromptIntent.Instruction, "append-instruction", true, false)]
    public void Resolve_ReturnsConfiguredRoute(PromptIntent intent, string command, bool requiresInstruction, bool requiresActor)
    {
        var router = new RolePlayPromptRouter();

        var route = router.Resolve(intent);

        Assert.Equal(intent, route.Intent);
        Assert.Equal(command, route.TargetCommand);
        Assert.Equal(requiresInstruction, route.RequiresInstructionPayload);
        Assert.Equal(requiresActor, route.RequiresActorContext);
    }

    [Fact]
    public void UnifiedPromptSubmission_InvalidWhenPromptTextMissing()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            SelectedIdentityId = "persona:you",
            SelectedIdentityType = IdentityOptionSource.Persona,
            PromptText = "   "
        };

        var valid = submission.IsValid(out var error);

        Assert.False(valid);
        Assert.Equal("PromptText is required.", error);
    }

    [Fact]
    public void UnifiedPromptSubmission_InstructionValidWithoutIdentity()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            PromptText = "Guide the next two interactions.",
            Intent = PromptIntent.Instruction
        };

        var valid = submission.IsValid(out var error);

        Assert.True(valid);
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void UnifiedPromptSubmission_InvalidWhenCustomIdentityHasNoName()
    {
        var submission = new UnifiedPromptSubmission
        {
            SessionId = "session-1",
            SelectedIdentityId = "manual",
            SelectedIdentityType = IdentityOptionSource.CustomCharacter,
            PromptText = "Continue this scene"
        };

        var valid = submission.IsValid(out var error);

        Assert.False(valid);
        Assert.Equal("Custom identity requires a custom name.", error);
    }
}
