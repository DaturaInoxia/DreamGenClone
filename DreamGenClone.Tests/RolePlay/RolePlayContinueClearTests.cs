using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinueClearTests
{
    [Fact]
    public async Task ContinueAsAsync_ClearRequest_ReturnsClearResult()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Continue clear");

        var result = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            IsClearAction = true,
            TriggeredBy = SubmissionSource.ContinueAsPopupContinue
        });

        Assert.True(result.Success);
        Assert.True(result.IsClearResult);
        Assert.Empty(result.ParticipantOutputs);
        Assert.Null(result.NarrativeOutput);
    }
}
