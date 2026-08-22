using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImageBeatAnalysisServiceTests
{
    private const string SoloBeat = "{\"beats\":[{\"schemaVersion\":3,\"beatId\":\"b1\",\"order\":1,\"label\":\"Arrival\",\"visualDescription\":\"She steps into the hall.\",\"interactionIds\":[\"n1\"],\"characters\":[{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"near the door\",\"actionOrObservation\":\"steps into the hall\",\"sightline\":\"forward\",\"visibleCharacterNames\":[],\"clothing\":\"blue shirt\"}],\"location\":\"hall\",\"timeOfDay\":\"evening\",\"lighting\":\"dim\",\"environment\":\"quiet hall\",\"mood\":\"expectant\"}]}";

    [Fact]
    public void ParseOutput_AcceptsCorrectlyEscapedControlCharactersInsideModelStrings()
    {
        var service = new SceneImageBeatAnalysisService();
        var rawOutput = "{\"beats\":[{\"schemaVersion\":3,\"beatId\":\"b1\",\"order\":1,\"label\":\"Arrival\",\"visualDescription\":\"She stepped closer.\\nHe raised a hand.\\tThe room went quiet.\",\"interactionIds\":[\"i1\"],\"characters\":[{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"near the door\",\"actionOrObservation\":\"steps closer\",\"sightline\":\"toward Husband\",\"visibleCharacterNames\":[\"Husband\"],\"clothing\":\"blue shirt\"},{\"name\":\"Husband\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"by the stairs\",\"actionOrObservation\":\"raises a hand\",\"sightline\":\"toward Wife\",\"visibleCharacterNames\":[\"Wife\"],\"clothing\":\"not established\"}],\"location\":\"hall\",\"timeOfDay\":\"evening\",\"lighting\":\"dim\",\"environment\":\"quiet hall\",\"mood\":\"tense\"}]}";

        var beats = service.ParseOutput(rawOutput, [new RolePlayInteraction { Id = "i1", ActorName = "Narrative" }]);

        Assert.Single(beats);
        Assert.Equal("She stepped closer.\nHe raised a hand.\tThe room went quiet.", beats[0].VisualDescription);
    }

    [Fact]
    public void ParseOutput_StillRejectsControlCharactersOutsideStrings()
    {
        var service = new SceneImageBeatAnalysisService();
        var rawOutput = "{\u0001\"beats\":[{\"schemaVersion\":2,\"beatId\":\"b1\",\"order\":1,\"label\":\"Arrival\",\"visualDescription\":\"A moment\",\"interactionIds\":[\"i1\"],\"characters\":[],\"location\":\"hall\",\"timeOfDay\":\"evening\",\"lighting\":\"dim\",\"environment\":\"hall\",\"mood\":\"quiet\"}]}";

        Assert.ThrowsAny<Exception>(() => service.ParseOutput(rawOutput, [new RolePlayInteraction { Id = "i1", ActorName = "Narrative" }]));
    }

    [Fact]
    public void ParseOutput_AllowsOneActiveCharacterAndNoObservers()
    {
        var service = new SceneImageBeatAnalysisService();

        var beat = Assert.Single(service.ParseOutput(
            SoloBeat,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Equal(3, beat.SchemaVersion);
        Assert.Equal("active", Assert.Single(beat.Characters).Involvement);
    }

    [Fact]
    public void ParseOutput_AllowsMultipleActiveCharactersAndNoObservers()
    {
        var service = new SceneImageBeatAnalysisService();
        var rawOutput = SoloBeat.Replace(
            "{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"near the door\",\"actionOrObservation\":\"steps into the hall\",\"sightline\":\"forward\",\"visibleCharacterNames\":[],\"clothing\":\"blue shirt\"}",
            "{\"name\":\"Wife\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"near the door\",\"actionOrObservation\":\"reaches out\",\"sightline\":\"toward Husband\",\"visibleCharacterNames\":[\"Husband\"],\"clothing\":\"blue shirt\"},{\"name\":\"Husband\",\"profileId\":null,\"involvement\":\"active\",\"physicalLocation\":\"hall\",\"position\":\"by the stairs\",\"actionOrObservation\":\"takes her hand\",\"sightline\":\"toward Wife\",\"visibleCharacterNames\":[\"Wife\"],\"clothing\":\"green shirt\"}");

        var beat = Assert.Single(service.ParseOutput(
            rawOutput,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Equal(2, beat.Characters.Count);
        Assert.All(beat.Characters, character => Assert.Equal("active", character.Involvement));
    }

    [Fact]
    public void ParseOutput_RejectsLegacySchema()
    {
        var service = new SceneImageBeatAnalysisService();
        var legacy = SoloBeat.Replace("\"schemaVersion\":3,", string.Empty);

        var error = Assert.Throws<InvalidOperationException>(() => service.ParseOutput(
            legacy,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_RejectsVisibilityOutsideBeat()
    {
        var service = new SceneImageBeatAnalysisService();
        var invalid = SoloBeat.Replace("\"visibleCharacterNames\":[]", "\"visibleCharacterNames\":[\"Absent\"]");

        var error = Assert.Throws<InvalidOperationException>(() => service.ParseOutput(
            invalid,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Contains("visible character", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_RejectsActiveCharacterOutsidePrimaryLocation()
    {
        var service = new SceneImageBeatAnalysisService();
        var invalid = SoloBeat.Replace(
            "\"location\":\"hall\"",
            "\"location\":\"trailer bedroom and outside maintenance shed\"");

        var error = Assert.Throws<InvalidOperationException>(() => service.ParseOutput(
            invalid,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Contains("combines multiple physical spaces", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_RejectsCompoundLocationEvenWhenActiveCharacterMatchesIt()
    {
        var service = new SceneImageBeatAnalysisService();
        var invalid = SoloBeat
            .Replace("\"physicalLocation\":\"hall\"", "\"physicalLocation\":\"trailer bedroom and outside maintenance shed\"")
            .Replace("\"location\":\"hall\"", "\"location\":\"trailer bedroom and outside maintenance shed\"");

        var error = Assert.Throws<InvalidOperationException>(() => service.ParseOutput(
            invalid,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Contains("exactly one active-event location", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseOutput_AllowsRemoteObserverAtDifferentPhysicalLocation()
    {
        var service = new SceneImageBeatAnalysisService();
        var withObserver = SoloBeat.Replace(
            "}],\"location\":\"hall\"",
            "},{\"name\":\"Observer\",\"profileId\":null,\"involvement\":\"observer\",\"physicalLocation\":\"outside shed\",\"position\":\"at the window\",\"actionOrObservation\":\"watches\",\"sightline\":\"through the hall window\",\"visibleCharacterNames\":[\"Wife\"],\"clothing\":\"coat\"}],\"location\":\"hall\"");

        var beat = Assert.Single(service.ParseOutput(
            withObserver,
            [new RolePlayInteraction { Id = "n1", ActorName = "Narrative" }]));

        Assert.Equal("outside shed", beat.Characters.Single(character => character.Name == "Observer").PhysicalLocation);
    }

    [Fact]
    public void BuildMessages_RequiresNarrativeSynthesis()
    {
        var service = new SceneImageBeatAnalysisService();
        var interaction = new RolePlayInteraction { Id = "i1", ActorName = "Wife", Content = "She enters." };

        var error = Assert.Throws<InvalidOperationException>(() => service.BuildMessages(
            new DreamGenClone.Web.Application.RolePlay.Models.FullTurnContext
            {
                Interactions = [interaction],
                SelectedInteraction = interaction
            },
            new RolePlaySession(),
            null));

        Assert.Contains("Narrative", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMessages_DefinesNarrativeFirstEnsembleGrouping()
    {
        var service = new SceneImageBeatAnalysisService();
        var narrative = new RolePlayInteraction { Id = "n1", ActorName = "Narrative", Content = "Wife moves while two people watch from different rooms." };

        var (system, _) = service.BuildMessages(
            new DreamGenClone.Web.Application.RolePlay.Models.FullTurnContext
            {
                Interactions = [narrative],
                SelectedInteraction = narrative,
                NarrativeInteraction = narrative
            },
            new RolePlaySession(),
            null);

        Assert.Contains("derive the chronological sequence", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observer-only establishing beat", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same action at the same narrative time", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("active only when", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never place a literal newline", system, StringComparison.OrdinalIgnoreCase);
    }
}