using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// B-085: covers the beat-budget mapping and the per-response beat stage
/// (open / escalate / resolve) that drive the generalized beat cursor.
/// </summary>
public sealed class BeatStyleCursorTests
{
    [Theory]
    [InlineData(BeatScope.Single, 1)]
    [InlineData(BeatScope.Short, 3)]
    [InlineData(BeatScope.Extended, 5)]
    public void GetBeatStyleTurnBudget_MapsBeatScopeToBudget(BeatScope scope, int expected)
    {
        Assert.Equal(expected, ContinuationMarkerCatalog.GetBeatStyleTurnBudget(scope));
    }

    [Theory]
    [InlineData(0, 3, "turn 1 of 3")]
    [InlineData(1, 3, "turn 2 of 3")]
    [InlineData(2, 3, "turn 3 of 3")]
    [InlineData(3, 3, "bring it to its climax")]
    [InlineData(0, 1, "single turn")]
    public void DescribeBeatStage_SelectsStageFromPositionAndBudget(int turnsInBeat, int budget, string expectedSubstring)
    {
        var stage = ContinuationMarkerCatalog.DescribeBeatStage(turnsInBeat, budget);
        Assert.Contains(expectedSubstring, stage);
    }
}
