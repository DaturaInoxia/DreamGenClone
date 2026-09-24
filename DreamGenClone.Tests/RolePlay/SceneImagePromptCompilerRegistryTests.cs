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
        var flux = new FluxSceneImagePromptCompiler(new SdxlSceneImagePromptBuilder());
        var qwen21 = new QwenImage21SceneImagePromptCompiler(new SdxlSceneImagePromptBuilder());
        var registry = new SceneImagePromptCompilerRegistry([pony, sdxl, flux, qwen21]);

        Assert.Same(pony, registry.Resolve(SceneImageModelFamily.Pony, SceneImagePromptDialect.PonyV6Tags));
        Assert.Same(sdxl, registry.Resolve(SceneImageModelFamily.Sdxl, SceneImagePromptDialect.SdxlNaturalLanguage));
        Assert.Same(flux, registry.Resolve(SceneImageModelFamily.Flux, SceneImagePromptDialect.FluxNaturalLanguage));
        Assert.Same(qwen21, registry.Resolve(SceneImageModelFamily.QwenImage21, SceneImagePromptDialect.NaturalLanguage));
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

    /// <summary>
    /// Every compiler must name the BUILDER DIALECT it needs by concrete type. Two builders implement
    /// <c>ISceneImageLLMPromptBuilder</c> (Pony tags and natural language), so DI can bind that interface to only
    /// one of them - and it is bound to the Pony tag builder. A compiler that injects the interface therefore
    /// compiles its family's prompt in whatever dialect DI happened to pick. That is exactly what happened to
    /// Qwen-Image-2.1 and the API family (reported 2026-09-24: with 2.1 selected "Generate Prompt" returned
    /// <c>score_9, score_8_up, ... rating_explicit, 1girl, ...</c> into the natural-language draft). This guard
    /// keeps the trap from returning for any future family.
    /// </summary>
    [Fact]
    public void NoCompiler_DependsOnTheAmbiguousPromptBuilderInterface()
    {
        var compilerTypes = typeof(SceneImagePromptCompilerRegistry).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(ISceneImagePromptCompiler).IsAssignableFrom(type))
            .ToList();

        Assert.NotEmpty(compilerTypes);

        foreach (var compilerType in compilerTypes)
        {
            foreach (var constructor in compilerType.GetConstructors())
            {
                Assert.DoesNotContain(
                    constructor.GetParameters(),
                    parameter => parameter.ParameterType == typeof(ISceneImageLLMPromptBuilder));
            }
        }
    }

    /// <summary>
    /// The natural-language families (Qwen-Image-2.1 and API image models) must compile the natural-language
    /// photography brief, and must not carry the Pony tag vocabulary, regardless of which builder DI hands out.
    /// </summary>
    [Fact]
    public void NaturalLanguageFamilies_CompileTheBrief_NotPonyTags()
    {
        var naturalLanguageBuilder = new SdxlSceneImagePromptBuilder();
        var ponyBuilder = new PonySceneImagePromptBuilder();
        var qwen21 = new QwenImage21SceneImagePromptCompiler(naturalLanguageBuilder);
        var api = new ApiSceneImagePromptCompiler(naturalLanguageBuilder);

        // Wiring proof by identity: neither compiler can hold the tag builder.
        Assert.Same(naturalLanguageBuilder, qwen21.PromptBuilder);
        Assert.Same(naturalLanguageBuilder, api.PromptBuilder);
        Assert.NotSame(ponyBuilder, qwen21.PromptBuilder);
        Assert.NotSame(ponyBuilder, api.PromptBuilder);

        var moment = CreateMoment();
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
        var session = new RolePlaySession { Id = "session-1", Title = "Compiler dialect proof" };
        var state = new AdaptiveScenarioState
        {
            CurrentPhase = NarrativePhase.BuildUp,
            CurrentSceneLocation = moment.Location,
            CurrentTimeOfDay = TimeOfDay.Evening
        };
        var settings = new SceneImageStudioSettings { Style = "cinematic", ImageSize = "1024x1024" };

        var expected = naturalLanguageBuilder.BuildMessages(
            session, fullTurn, state, settings, ImageContentPolicy.AdultAllowed,
            null, null, selectedBeat: moment, pov: SceneImagePovFramer.Omniscient);
        var pony = ponyBuilder.BuildMessages(
            session, fullTurn, state, settings, ImageContentPolicy.AdultAllowed,
            null, null, selectedBeat: moment, pov: SceneImagePovFramer.Omniscient);

        // The drafted dialect, not the render workflow, is what went wrong: the tag system prompt and the brief
        // system prompt must differ, and the natural-language families must produce the brief.
        Assert.NotEqual(pony.SystemPrompt, expected.SystemPrompt);
        Assert.Contains("score_9", pony.SystemPrompt, StringComparison.Ordinal);

        foreach (var compiler in new ISceneImagePromptCompiler[] { qwen21, api })
        {
            var messages = compiler.PromptBuilder.BuildMessages(
                session, fullTurn, state, settings, ImageContentPolicy.AdultAllowed,
                null, null, selectedBeat: moment, pov: SceneImagePovFramer.Omniscient);

            Assert.Equal(expected.SystemPrompt, messages.SystemPrompt);
            Assert.Equal(expected.UserPrompt, messages.UserPrompt);
        }
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