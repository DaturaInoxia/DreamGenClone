using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneBeatCatalogueSnapshotBuilderTests
{
    private readonly SceneBeatCatalogueSnapshotBuilder _builder = new();

    [Fact]
    public void Build_FreezesAuthoritativeTurnAndAssignsDeterministicKeys()
    {
        var fixture = CreateFixture();

        var snapshot = _builder.Build(fixture.FullTurn, fixture.Session, fixture.Characters);

        Assert.Equal(SceneBeatCatalogueSnapshotBuilder.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal("session-1", snapshot.SessionId);
        Assert.Equal("turn-1", snapshot.TurnId);
        Assert.Equal(["n0", "c1", "c2"], snapshot.Evidence.Select(item => item.Key));
        Assert.Equal(["narrative-1", "input-1", "character-1"], snapshot.Evidence.Select(item => item.InteractionId));
        Assert.Equal([2, 0, 1], snapshot.Evidence.Select(item => item.SourceOrder));
        Assert.All(snapshot.Evidence, item => Assert.Equal(64, item.SourceSha256.Length));
        Assert.Equal(["p0", "p1"], snapshot.Profiles.Select(item => item.Key));
        Assert.Equal(["Becky", "Dean"], snapshot.Profiles.Select(item => item.Name));
        Assert.Equal("character-becky", snapshot.Profiles[0].CharacterId);
        Assert.Equal("character-dean", snapshot.Profiles[1].CharacterId);
        Assert.True(snapshot.Profiles[1].IsPersona);
        Assert.Equal(64, snapshot.TurnMembershipSha256.Length);

        fixture.FullTurn.Interactions[0].Content = "mutated after snapshot";
        fixture.Characters[0].Description = "mutated after snapshot";
        Assert.Equal("Narrative synthesis", snapshot.Evidence[0].Content);
        Assert.Equal("Scenario Becky", snapshot.Profiles[0].Description);
    }

    [Fact]
    public void Build_IsDeterministicAcrossLoadedInteractionOrder()
    {
        var first = CreateFixture();
        var second = CreateFixture();
        second.FullTurn = second.FullTurn with
        {
            Interactions = second.FullTurn.Interactions.Reverse().ToList()
        };

        var firstJson = _builder.Serialize(_builder.Build(first.FullTurn, first.Session, first.Characters));
        var secondJson = _builder.Serialize(_builder.Build(second.FullTurn, second.Session, second.Characters));

        Assert.Equal(firstJson, secondJson);
    }

    [Fact]
    public void Build_MissingAuthoritativeMember_FailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.FullTurn = fixture.FullTurn with
        {
            Interactions = fixture.FullTurn.Interactions.Where(item => item.Id != "character-1").ToList()
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            _builder.Build(fixture.FullTurn, fixture.Session, fixture.Characters));

        Assert.Contains("character-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithoutNarrative_FailsExplicitly()
    {
        var fixture = CreateFixture();
        fixture.FullTurn.Interactions.Single(item => item.Id == "narrative-1").ActorName = "Observer";

        var error = Assert.Throws<InvalidOperationException>(() =>
            _builder.Build(fixture.FullTurn, fixture.Session, fixture.Characters));

        Assert.Contains("exactly one authoritative Narrative", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveKeys_ReturnsAuthoritativeIdsAndRejectsUnknownKeys()
    {
        var fixture = CreateFixture();
        var snapshot = _builder.Build(fixture.FullTurn, fixture.Session, fixture.Characters);

        Assert.Equal(
            ["narrative-1", "character-1"],
            _builder.ResolveEvidenceInteractionIds(snapshot, ["n0", "c2"]));
        Assert.Equal(
            ["character-dean"],
            _builder.ResolveProfileCharacterIds(snapshot, ["p1"]));

        var evidenceError = Assert.Throws<InvalidOperationException>(() =>
            _builder.ResolveEvidenceInteractionIds(snapshot, ["n0", "missing"]));
        Assert.Contains("Unknown evidence keys: missing", evidenceError.Message, StringComparison.Ordinal);

        var duplicateError = Assert.Throws<InvalidOperationException>(() =>
            _builder.ResolveProfileCharacterIds(snapshot, ["p0", "p0"]));
        Assert.Contains("profile keys must be non-empty and unique", duplicateError.Message, StringComparison.Ordinal);
    }

    private static Fixture CreateFixture()
    {
        var started = new DateTime(2026, 8, 31, 10, 0, 0, DateTimeKind.Utc);
        var input = Interaction("input-1", "Dean", InteractionType.User, "User input", started.AddSeconds(1));
        var character = Interaction("character-1", "Becky", InteractionType.Npc, "Character response", started.AddSeconds(2));
        var narrative = Interaction("narrative-1", "Narrative", InteractionType.System, "Narrative synthesis", started.AddSeconds(3));
        var turn = new RolePlayTurn
        {
            TurnId = "turn-1",
            SessionId = "session-1",
            TurnIndex = 7,
            TurnKind = "SubmitPrompt",
            TriggerSource = "Send",
            InputInteractionId = input.Id,
            OutputInteractionIds = [character.Id, narrative.Id],
            StartedUtc = started,
            CompletedUtc = started.AddSeconds(4),
            Status = RolePlayTurnStatus.Completed
        };
        return new Fixture
        {
            Session = new RolePlaySession
            {
                Id = "session-1",
                PersonaCharacterId = "character-dean",
                PersonaName = "Dean",
                PersonaRole = "Husband",
                PersonaGender = "Male",
                PersonaDescription = "Session Dean"
            },
            FullTurn = new FullTurnContext
            {
                Turn = turn,
                Interactions = [narrative, input, character],
                SelectedInteraction = character,
                NarrativeInteraction = narrative
            },
            Characters =
            [
                new Character
                {
                    Id = "character-becky",
                    Name = "Becky",
                    Role = "Wife",
                    Gender = "Female",
                    Description = "Scenario Becky"
                },
                new Character
                {
                    Id = "character-dean",
                    Name = "Dean",
                    Role = "Husband",
                    Gender = "Male",
                    Description = "Scenario Dean"
                }
            ]
        };
    }

    private static RolePlayInteraction Interaction(
        string id,
        string actor,
        InteractionType type,
        string content,
        DateTime createdAt)
        => new()
        {
            Id = id,
            ActorName = actor,
            InteractionType = type,
            Content = content,
            CreatedAt = createdAt
        };

    private sealed class Fixture
    {
        public required RolePlaySession Session { get; init; }
        public required FullTurnContext FullTurn { get; set; }
        public required List<Character> Characters { get; init; }
    }
}