using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// B-080 tests for <see cref="RolePlayEngineService.GuaranteeParticipationSeats"/> — the
/// participation post-guard that guarantees eligible in-scene and Required-affinity candidates
/// receive a seat even when the LLM actor-selection under-selects. It only ADDS seats, never
/// removes LLM choices, and respects hard filters (AffinityStatus.Excluded,
/// ParticipateInAutoContinue=false).
/// </summary>
public sealed class RolePlayParticipationGuardTests
{
    private static (RolePlayEngineService.AvailableCharacter Character, double Score) Scored(
        string name, bool inScene, RolePlayEngineService.AffinityStatus affinity, double score)
        => (new RolePlayEngineService.AvailableCharacter(name, null, inScene, affinity, null, true, null), score);

    private static List<(RolePlayEngineService.AvailableCharacter Character, double Score)> Descending(
        params (RolePlayEngineService.AvailableCharacter Character, double Score)[] entries)
        => entries.OrderByDescending(x => x.Score).ToList();

    private static Dictionary<string, CharacterTurnOverride> NoOverrides() => new();

    private static Dictionary<string, CharacterTurnOverride> WithOverride(string name, bool participate)
        => new()
        {
            [name] = new CharacterTurnOverride { CharacterName = name, ParticipateInAutoContinue = participate },
        };

    [Fact]
    public void Guard_UnderSelectingLlm_GetsInSceneCandidateAdded()
    {
        // The B-080 bug shape: LLM returned only Dean, but Becky is in-scene (via line-of-sight).
        // The guard must add Becky so the Wife reacts to the OtherMan's action.
        var participants = new List<string> { "Dean" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100),
            Scored("Ken", inScene: false, RolePlayEngineService.AffinityStatus.None, 200));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.Equal(new[] { "Dean", "Becky" }, result);
        Assert.DoesNotContain("Ken", result); // out-of-scene and not Required → not forced in
    }

    [Fact]
    public void Guard_InSceneCandidateAddedUpToDesiredCount()
    {
        var participants = new List<string> { "Dean" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100),
            Scored("Ken", inScene: true, RolePlayEngineService.AffinityStatus.None, 90));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 2);

        Assert.Equal(2, result.Count);
        Assert.Contains("Dean", result);
        Assert.Contains("Becky", result);
        Assert.DoesNotContain("Ken", result); // capped at desiredCount
    }

    [Fact]
    public void Guard_RequiredAffinityCandidateAddedEvenIfOutOfScene()
    {
        var participants = new List<string> { "Dean" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: false, RolePlayEngineService.AffinityStatus.Required, 1200));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.Contains("Becky", result);
    }

    [Fact]
    public void Guard_ExcludedCandidateNeverAdded()
    {
        var participants = new List<string> { "Dean" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Excluded, 100));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.DoesNotContain("Becky", result);
    }

    [Fact]
    public void Guard_ParticipateInAutoContinueFalseCandidateNeverAdded()
    {
        var participants = new List<string> { "Dean" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, WithOverride("Becky", participate: false), desiredCount: 3);

        Assert.DoesNotContain("Becky", result);
    }

    [Fact]
    public void Guard_NeverRemovesLlmChoices()
    {
        // LLM chose Ken (out-of-scene) — the guard must keep him, only ADD in-scene/Required.
        var participants = new List<string> { "Ken" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100),
            Scored("Ken", inScene: false, RolePlayEngineService.AffinityStatus.None, 200));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.Contains("Ken", result);
        Assert.Contains("Dean", result);
        Assert.Contains("Becky", result);
    }

    [Fact]
    public void Guard_AlreadySelectedCandidatesAreNotDuplicated()
    {
        var participants = new List<string> { "Dean", "Becky" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Guard_FullParticipation_NoChangesMade()
    {
        var participants = new List<string> { "Dean", "Becky", "Ken" };
        var scored = Descending(
            Scored("Dean", inScene: true, RolePlayEngineService.AffinityStatus.Required, 1500),
            Scored("Becky", inScene: true, RolePlayEngineService.AffinityStatus.Preferred, 100),
            Scored("Ken", inScene: true, RolePlayEngineService.AffinityStatus.None, 200));

        var result = RolePlayEngineService.GuaranteeParticipationSeats(
            participants, scored, NoOverrides(), desiredCount: 3);

        Assert.Equal(new[] { "Dean", "Becky", "Ken" }, result);
    }
}
