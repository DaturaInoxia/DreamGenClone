using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayInstructionFlowTests
{
    [Fact]
    public async Task SubmitPromptAsync_InstructionWithoutCharacter_AddsVisibleInstructionInteraction()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Instruction Flow");

        var submission = new UnifiedPromptSubmission
        {
            SessionId = session.Id,
            Intent = PromptIntent.Instruction,
            PromptText = "Push the story toward conflict.",
            SubmittedVia = SubmissionSource.PlusButton
        };

        var interaction = await service.SubmitPromptAsync(submission);

        Assert.Equal(InteractionType.System, interaction.InteractionType);
        Assert.Equal("Instruction", interaction.ActorName);
        Assert.Equal("Push the story toward conflict.", interaction.Content);
    }
}
