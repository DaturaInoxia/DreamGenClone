using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

/// <summary>
/// Tests for CR-006 P1 — presence-grounded participant resolution
/// (<see cref="SceneImageParticipantResolver"/>).
/// </summary>
public sealed class SceneImageParticipantResolverTests
{
    private static RolePlaySession MakeSession() => new()
    {
        Id = "s1",
        Title = "Test",
        PersonaName = "Ken"
    };

    private static RolePlayInteraction MakeInteraction(string actor = "Becky", string content = "She stepped closer to him.")
        => new() { Id = "i1", ActorName = actor, Content = content };

    private static AdaptiveScenarioState MakeState() => new()
    {
        CurrentPhase = NarrativePhase.BuildUp,
        CurrentSceneLocation = "living room",
        CurrentTimeOfDay = TimeOfDay.Evening
    };

    /// <summary>
    /// In production the preprocessor passes <c>session.AdaptiveState</c> as the scenario state,
    /// so the presence helper (which reads <c>session.AdaptiveState</c>) sees the same object.
    /// Tests mirror that by wiring the state into the session.
    /// </summary>
    private static RolePlaySession MakeSessionWithState(AdaptiveScenarioState state)
    {
        var session = MakeSession();
        session.AdaptiveState = state;
        return session;
    }

    private static List<Character> MakeCharacters() => new()
    {
        new() { Id = "c1", Name = "Becky", Role = "Wife" },
        new() { Id = "c2", Name = "Dean", Role = "OtherMan" },
        new() { Id = "c3", Name = "Ken", Role = "Husband", IsPersona = true }
    };

    [Fact]
    public void ResolveParticipants_ActorAlwaysIncluded()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Becky" && p.Presence == SceneImageParticipantResolver.Presence.Actor);
    }

    [Fact]
    public void ResolveParticipants_InSceneCharacter_Included()
    {
        var state = MakeState();
        var session = MakeSessionWithState(state);
        var interaction = MakeInteraction(actor: "Becky");
        // Dean is at the current scene location → present.
        state.CharacterLocations.Add(new CharacterLocationState
        {
            CharacterId = "Dean",
            TrueLocation = "living room"
        });

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Dean" && p.Presence == SceneImageParticipantResolver.Presence.InScene);
    }

    [Fact]
    public void ResolveParticipants_InEncounterCharacter_Included()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky");
        var state = MakeState();
        state.CharacterEncounterStates["Dean"] = new CharacterEncounterState { IsHavingSexConfirmed = true };

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Dean" && p.Presence == SceneImageParticipantResolver.Presence.InEncounter);
    }

    [Fact]
    public void ResolveParticipants_NamedInText_Included_OnlyAfterPresence()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky", content: "Becky and Dean were in the bedroom.");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        // Dean is named in text → included as NamedInText (no presence data).
        Assert.Contains(result, p => p.Name == "Dean" && p.Presence == SceneImageParticipantResolver.Presence.NamedInText);
    }

    [Fact]
    public void ResolveParticipants_PersonaExcluded_WhenNotPresent()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        // Ken (persona) is not present → excluded entirely.
        Assert.DoesNotContain(result, p => p.Name == "Ken");
    }

    [Fact]
    public void ResolveParticipants_PersonaIncluded_WhenInEncounter()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky");
        var state = MakeState();
        state.CharacterEncounterStates["Ken"] = new CharacterEncounterState { IsHavingSex = true };

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Ken" && p.Presence == SceneImageParticipantResolver.Presence.InEncounter);
    }

    [Fact]
    public void ResolveParticipants_PersonaIncluded_WhenActor()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Ken");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Ken" && p.Presence == SceneImageParticipantResolver.Presence.Actor);
    }

    [Fact]
    public void ResolveParticipants_PersonaIncluded_WhenNamedInText()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky", content: "Ken watched from the doorway.");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        Assert.Contains(result, p => p.Name == "Ken" && p.Presence == SceneImageParticipantResolver.Presence.NamedInText);
    }

    [Fact]
    public void ResolveParticipants_NoCharacters_ReturnsActorOnly()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, null);

        Assert.Single(result);
        Assert.Equal("Becky", result[0].Name);
    }

    [Fact]
    public void ResolveParticipants_ObserverPersona_NotIncludedAsSubject()
    {
        var session = MakeSession();
        var interaction = MakeInteraction(actor: "Becky", content: "Ken watched from the doorway.");
        var state = MakeState();

        var result = SceneImageParticipantResolver.ResolveParticipants(session, interaction, state, MakeCharacters());

        // Ken is named in text → included as NamedInText (an observer, not a subject).
        Assert.Contains(result, p => p.Name == "Ken" && p.Presence == SceneImageParticipantResolver.Presence.NamedInText);
    }
}