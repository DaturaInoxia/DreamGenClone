using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

public sealed class RolePlayContinueParityTests
{
    [Fact]
    public async Task ContinueAsAsync_PopupAndOverflowUseSameContinuationSemantics()
    {
        var service = RolePlayTestFactory.CreateEngineService();
        var session = await service.CreateSessionAsync("Continue parity");

        var popup = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            SelectedParticipants = [ContinueAsActor.You, ContinueAsActor.Npc],
            IncludeNarrative = true,
            TriggeredBy = SubmissionSource.ContinueAsPopupContinue
        });

        var overflow = await service.ContinueAsAsync(new ContinueAsRequest
        {
            SessionId = session.Id,
            SelectedParticipants = [ContinueAsActor.You, ContinueAsActor.Npc],
            IncludeNarrative = true,
            TriggeredBy = SubmissionSource.MainOverflowContinue
        });

        Assert.True(popup.Success);
        Assert.True(overflow.Success);
        Assert.Equal(popup.ParticipantOutputs.Count, overflow.ParticipantOutputs.Count);
        Assert.Equal(popup.NarrativeOutput is not null, overflow.NarrativeOutput is not null);
    }
}
