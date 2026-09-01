using System.Text.Json;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Models;
using DreamGenClone.Web.Domain.RolePlay;

namespace DreamGenClone.Tests.RolePlay;

public sealed class SceneImagePromptCompilerRegistryTests
{
    [Fact]
    public void Resolve_ExactPair_ReturnsOnlyMatchingCompiler()
    {
        var pony = new PonySceneImagePromptCompiler(new PonySceneImagePromptBuilder());
        var sdxl = new SdxlSceneImagePromptCompiler(new SdxlSceneImagePromptBuilder());
        var registry = new SceneImagePromptCompilerRegistry([pony, sdxl]);

        Assert.Same(pony, registry.Resolve(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));
        Assert.Same(sdxl, registry.Resolve(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage));
    }

    [Fact]
    public void Resolve_UnregisteredPair_FailsFast()
    {
        var registry = new SceneImagePromptCompilerRegistry(
            [new PonySceneImagePromptCompiler(new PonySceneImagePromptBuilder())]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage));

        Assert.Contains("No scene-image prompt compiler", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_DuplicatePair_FailsFast()
    {
        var registry = new SceneImagePromptCompilerRegistry(
        [
            new PonySceneImagePromptCompiler(new PonySceneImagePromptBuilder()),
            new PonySceneImagePromptCompiler(new PonySceneImagePromptBuilder())
        ]);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));

        Assert.Contains("Multiple scene-image prompt compilers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameMoment_CompilesThroughBothFamilies_WithoutMutatingInput()
    {
        var registry = new SceneImagePromptCompilerRegistry(
        [
            new PonySceneImagePromptCompiler(new PonySceneImagePromptBuilder()),
            new SdxlSceneImagePromptCompiler(new SdxlSceneImagePromptBuilder())
        ]);
        var moment = CreateMoment();
        var snapshot = JsonSerializer.Serialize(moment);
        var selected = new RolePlayInteraction
        {
            Id = "interaction-1",
            ActorName = "Becky",
            Content = moment.VisualDescription
        };
        var fullTurn = new FullTurnContext
        {
            Interactions = [selected],
            SelectedInteraction = selected
        };
        var session = new RolePlaySession { Id = "session-1", Title = "Compiler proof" };
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = NarrativePhase.BuildUp,
            CurrentSceneLocation = moment.Location,
            CurrentTimeOfDay = TimeOfDay.Evening
        };
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024" };

        var pony = registry.Resolve(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags);
        var sdxl = registry.Resolve(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage);
        var ponyMessages = pony.PromptBuilder.BuildMessages(
            session, fullTurn, state, settings, ImageContentPolicy.AdultAllowed,
            null, null, selectedBeat: moment, pov: SceneImagePovFramer.Omniscient);
        var sdxlMessages = sdxl.PromptBuilder.BuildMessages(
            session, fullTurn, state, settings, ImageContentPolicy.AdultAllowed,
            null, null, selectedBeat: moment, pov: SceneImagePovFramer.Omniscient);

        Assert.Contains("score_9", ponyMessages.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("natural-language", sdxlMessages.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(pony.BuildNegativePrompt(moment, SceneImagePovFramer.Omniscient),
            sdxl.BuildNegativePrompt(moment, SceneImagePovFramer.Omniscient));
        Assert.Equal(snapshot, JsonSerializer.Serialize(moment));
    }

    private static SceneImageBeat CreateMoment() => new()
    {
        SchemaVersion = SceneImageBeatAnalysisService.CurrentSchemaVersion,
        BeatId = "moment-1",
        Label = "Shared Moment",
        VisualDescription = "Becky and Dean pause beside the rain-lit window.",
        Location = "living room",
        TimeOfDay = "evening",
        Lighting = "warm lamp light and cool window light",
        Environment = "a quiet living room during rainfall",
        Mood = "tense and intimate",
        Characters =
        [
            new SceneImageBeatCharacter
            {
                Name = "Becky",
                Involvement = "active",
                PhysicalLocation = "living room",
                Position = "beside the window",
                ActionOrObservation = "faces Dean",
                Sightline = "toward Dean",
                VisibleCharacterNames = ["Dean"],
                Clothing = "yellow dress"
            },
            new SceneImageBeatCharacter
            {
                Name = "Dean",
                Involvement = "active",
                PhysicalLocation = "living room",
                Position = "beside Becky",
                ActionOrObservation = "meets Becky's gaze",
                Sightline = "toward Becky",
                VisibleCharacterNames = ["Becky"],
                Clothing = "black shirt"
            }
        ]
    };
}