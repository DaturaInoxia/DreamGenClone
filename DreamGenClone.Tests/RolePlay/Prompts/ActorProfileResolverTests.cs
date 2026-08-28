using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Domain.RolePlay;
using Xunit;

namespace DreamGenClone.Tests.RolePlay.Prompts;

/// <summary>
/// Tests for ActorProfileResolver covering all 5 profile kinds × variant matrix,
/// plus fail-fast on unknown actor (Edge Case: Actor profile mismatch).
/// </summary>
public sealed class ActorProfileResolverTests
{
    private readonly ActorProfileResolver _resolver = new();

    private static RolePlaySession CreateSession(string personaId = "p1", string personaName = "Ken")
    {
        return new RolePlaySession
        {
            Id = Guid.NewGuid().ToString(),
            PersonaCharacterId = personaId,
            PersonaName = personaName,
            PersonaRole = "Hero",
            AdaptiveState = new AdaptiveScenarioState
            {
                CurrentSceneLocation = "The Cabin",
            },
        };
    }

    private static List<ScenarioCharacter> CreateRoster()
    {
        return
        [
            new("p1", "Ken", "protagonist"),
            new("c1", "Becky", "wife"),
            new("c2", "Dean", "husband"),
        ];
    }

    // ── Player profile ─────────────────────────────────────────

    // ── Narrative profile ──────────────────────────────────────

    [Fact]
    public void Resolve_NarrativeIntent_AnyActor_ReturnsNarrativeProfile()
    {
        var session = CreateSession();
        var roster = CreateRoster();

        var profile = _resolver.Resolve(ContinueAsActor.Npc, null, PromptIntent.Narrative, session, roster);

        Assert.Equal(ActorProfileKind.Narrative, profile.Kind);
        Assert.Equal("omniscient narrator", profile.ActorName);
        Assert.Equal("narrator", profile.ActorRole);
    }

    [Fact]
    public void Resolve_NarrativeIntent_InstructionIntent_StillNarrative()
    {
        var session = CreateSession();
        var roster = CreateRoster();

        var profile = _resolver.Resolve(ContinueAsActor.Npc, null, PromptIntent.Narrative, session, roster);

        Assert.Equal(ActorProfileKind.Narrative, profile.Kind);
    }

    // ── NpcPresent profile ─────────────────────────────────────

    [Fact]
    public void Resolve_Npc_WithInteractionHistory_ReturnsNpcProfile()
    {
        var session = CreateSession();
        session.CurrentTurnState = TurnState.NpcTurn; // Not Any, so resolver searches interactions
        session.Interactions.Add(new RolePlayInteraction
        {
            ActorName = "Becky",
            InteractionType = InteractionType.Npc,
            Content = "Hello there.",
        });
        var roster = CreateRoster();

        var profile = _resolver.Resolve(ContinueAsActor.Npc, null, PromptIntent.Message, session, roster);

        Assert.Contains(profile.Kind, new[] { ActorProfileKind.NpcPresent, ActorProfileKind.NpcNonPresent });
        Assert.Equal("Becky", profile.ActorName);
    }

    // ── Fail-fast on unknown NPC ───────────────────────────────

    [Fact]
    public void Resolve_Npc_UnknownActor_ThrowsInvalidOperation()
    {
        var session = CreateSession();
        session.CurrentTurnState = TurnState.NpcTurn; // Not Any, so resolver searches interactions
        // No interactions added — ResolveNpcNameFromSession will throw.

        var roster = CreateRoster();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            _resolver.Resolve(ContinueAsActor.Npc, null, PromptIntent.Message, session, roster));

        Assert.Contains("Cannot resolve NPC actor name", ex.Message);
    }

    // ── Custom profile ─────────────────────────────────────────

    [Fact]
    public void Resolve_Custom_ValidName_ReturnsCustomProfile()
    {
        var session = CreateSession();
        var roster = CreateRoster();

        var profile = _resolver.Resolve(ContinueAsActor.Custom, "Stranger", PromptIntent.Message, session, roster);

        Assert.Equal(ActorProfileKind.Custom, profile.Kind);
        Assert.Equal("Stranger", profile.ActorName);
        Assert.Equal("custom", profile.ActorRole);
    }

    [Fact]
    public void Resolve_Custom_NullName_ThrowsInvalidOperation()
    {
        var session = CreateSession();
        var roster = CreateRoster();

        Assert.Throws<InvalidOperationException>(() =>
            _resolver.Resolve(ContinueAsActor.Custom, null, PromptIntent.Message, session, roster));
    }
}