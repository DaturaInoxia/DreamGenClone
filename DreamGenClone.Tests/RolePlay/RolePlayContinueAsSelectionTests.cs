using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinueAsSelectionTests
{
    [Fact]
    public async Task ContinueAsAsync_SelectedIdentityIds_HonorsAvailability()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Continue As");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            SelectedIdentityIds = ["custom:adhoc", "persona:you"],
            IncludeNarrative = false
        });

        Assert.True(result.Success);
        Assert.Collection(
            result.ParticipantOutputs,
            item => Assert.Equal("You", item.ActorName));
    }

    [Fact]
    public async Task ContinueAsAsync_NoSelection_UsesContextDrivenFallback()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Fallback continue");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(result.Success);
        Assert.Single(result.ParticipantOutputs);
    }
}
