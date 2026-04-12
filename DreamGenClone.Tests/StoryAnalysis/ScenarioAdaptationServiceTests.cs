using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.StoryParser.Models;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DreamGenClone.Tests.StoryAnalysis;

public class ScenarioAdaptationServiceTests
{
    private static readonly string ValidLlmResponse = """
        {
          "plot": {
            "title": "Fling with Fitness Trainer",
            "description": "A married woman begins private sessions with a charismatic personal trainer at her gym. The workouts become increasingly hands-on as the chemistry between them builds.",
            "conflicts": ["Guilt vs desire", "Loyalty to spouse vs new attraction"],
            "goals": ["Explore forbidden attraction", "Maintain the secret"]
          },
          "setting": {
            "worldDescription": "A modern upscale gym and surrounding suburban neighborhood",
            "timeFrame": "Present day",
            "environmentalDetails": ["Private training room", "Steam room"],
            "worldRules": ["Characters maintain public appearances"]
          },
          "style": {
            "tone": "Sensual and tension-filled",
            "writingStyle": "Descriptive with internal monologue",
            "pointOfView": "Third person limited",
            "styleGuidelines": ["Build tension gradually", "Use physical descriptions"]
          },
          "characters": [
            {
              "name": "Becky",
              "description": "A married woman who signed up for personal training to get back in shape, but finds herself drawn to her trainer.",
              "role": "protagonist"
            },
            {
              "name": "Ken",
              "description": "Becky's husband, oblivious to the growing attraction between his wife and her trainer.",
              "role": "supporting"
            }
          ],
          "locations": [
            { "name": "Elite Fitness Gym", "description": "An upscale gym with private training rooms" }
          ],
          "openings": [
            { "title": "First Session", "text": "Becky adjusts her ponytail in the mirror of the private training room as she waits for her first session with the new trainer everyone has been talking about." }
          ],
          "characterMappings": [
            { "originalName": "Sarah", "substitutedName": "Becky", "role": "protagonist" },
            { "originalName": "Mike", "substitutedName": "Ken", "role": "supporting" }
          ],
          "adaptationNotes": "Adapted the fitness trainer seduction concept. Mapped the original protagonist to Becky and her husband to Ken."
        }
        """;

    private readonly Guid _beckyTemplateId = Guid.NewGuid();
    private readonly Guid _kenTemplateId = Guid.NewGuid();

    private ScenarioAdaptationService CreateService(
        string? llmResponse = null,
        ParsedStoryDetail? storyDetail = null,
        StorySummary? summary = null,
        StoryAnalysisResult? analysis = null,
        List<TemplateDefinition>? templates = null)
    {
        var storyParser = new FakeStoryParserService(storyDetail);
        var analysisService = new FakeStoryAnalysisService(analysis);
        var summaryService = new FakeStorySummaryService(summary);
        var templateService = new FakeTemplateService(templates ?? []);
        var completionClient = new FakeCompletionClient(llmResponse ?? ValidLlmResponse);
        var modelResolver = new FakeModelResolutionService();
        var logger = NullLogger<ScenarioAdaptationService>.Instance;

        return new ScenarioAdaptationService(
            storyParser, analysisService, summaryService, templateService,
            completionClient, modelResolver, logger);
    }

    private ParsedStoryDetail CreateStoryDetail() => new()
    {
        Id = "story-1",
        Title = "The Gym Story",
        Author = "Test Author",
        SourceUrl = "https://example.com/story",
        PageCount = 1,
        CombinedText = "Story text here..."
    };

    private StorySummary CreateSummary() => new()
    {
        ParsedStoryId = "story-1",
        SummaryText = "A woman named Sarah joins a gym and becomes attracted to her fitness trainer. Her husband Mike remains unaware of the growing connection."
    };

    private StoryAnalysisResult CreateAnalysis() => new()
    {
        ParsedStoryId = "story-1",
        CharactersJson = """[{"name":"Sarah","role":"protagonist","description":"The wife"},{"name":"Mike","role":"supporting","description":"The husband"}]""",
        ThemesJson = """[{"name":"Forbidden attraction","description":"Growing desire outside marriage","prevalence":"primary"}]""",
        PlotStructureJson = """{"exposition":"Sarah joins gym","risingAction":"Chemistry builds","climax":"First encounter","fallingAction":"Guilt sets in","resolution":"Decision made"}""",
        WritingStyleJson = """{"tone":"Sensual","perspective":"Third person","pacing":"Slow burn","languageComplexity":"Moderate","notableDevices":["Internal monologue"]}"""
    };

    private List<TemplateDefinition> CreateTemplates() =>
    [
        new()
        {
            Id = _beckyTemplateId,
            TemplateType = TemplateType.Character,
            Name = "Becky",
            Content = "Becky is Ken's wife. 50 years old, 5'8\". A good person seeking connection."
        },
        new()
        {
            Id = _kenTemplateId,
            TemplateType = TemplateType.Character,
            Name = "Ken",
            Content = "Ken is Becky's husband of 20 years. Complacent but loving."
        }
    ];

    private AdaptStoryRequest CreateRequest() => new()
    {
        ParsedStoryId = "story-1",
        CharacterSubstitutions =
        [
            new() { TemplateId = _beckyTemplateId, TargetRole = "protagonist" },
            new() { TemplateId = _kenTemplateId, TargetRole = "supporting" }
        ]
    };

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_ReturnsValidScenario()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.NotNull(result.GeneratedScenario);
        Assert.Equal("Fling with Fitness Trainer", result.GeneratedScenario!.Name);
        Assert.Equal("story-1", result.SourceParsedStoryId);
        Assert.Equal("The Gym Story", result.SourceStoryTitle);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_PlotIsCharacterAgnostic()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        var plot = result.GeneratedScenario!.Plot;
        Assert.Equal("Fling with Fitness Trainer", plot.Title);
        Assert.DoesNotContain("Becky", plot.Description!);
        Assert.DoesNotContain("Ken", plot.Description!);
        Assert.NotEmpty(plot.Conflicts);
        Assert.NotEmpty(plot.Goals);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_CharactersAreBeckyAndKen()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        var characters = result.GeneratedScenario!.Characters;
        Assert.Equal(2, characters.Count);
        Assert.Contains(characters, c => c.Name == "Becky");
        Assert.Contains(characters, c => c.Name == "Ken");

        // Becky should be linked to her template
        var becky = characters.First(c => c.Name == "Becky");
        Assert.Equal(_beckyTemplateId.ToString(), becky.TemplateId);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_HasCharacterMappings()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Equal(2, result.CharacterMappings.Count);
        Assert.Contains(result.CharacterMappings, m => m.OriginalName == "Sarah" && m.SubstitutedName == "Becky");
        Assert.Contains(result.CharacterMappings, m => m.OriginalName == "Mike" && m.SubstitutedName == "Ken");
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_HasOpeningWithCharacterNames()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Single(result.GeneratedScenario!.Openings);
        Assert.Contains("Becky", result.GeneratedScenario.Openings[0].Text!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Success_DescriptionContainsProvenance()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.Contains("Adapted from: The Gym Story", result.GeneratedScenario!.Description!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_StoryNotFound_ReturnsError()
    {
        var service = CreateService(
            storyDetail: null,
            summary: CreateSummary(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_NoSummary_ReturnsError()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: null,
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Contains("summarized", result.ErrorMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_NoCharacterTemplates_ReturnsError()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            templates: []);

        var request = new AdaptStoryRequest
        {
            ParsedStoryId = "story-1",
            CharacterSubstitutions =
            [
                new() { TemplateId = Guid.NewGuid(), TargetRole = "protagonist" }
            ]
        };

        var result = await service.AdaptStoryToScenarioAsync(request);

        Assert.False(result.Success);
        Assert.Contains("character template", result.ErrorMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_WithoutAnalysis_StillSucceeds()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: null,
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.NotNull(result.GeneratedScenario);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_InvalidJsonResponse_ReturnsError()
    {
        var service = CreateService(
            llmResponse: "This is not valid JSON at all",
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.False(result.Success);
        Assert.Contains("Failed to parse", result.ErrorMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_JsonWithCodeFences_ParsesSuccessfully()
    {
        var wrappedResponse = $"```json\n{ValidLlmResponse}\n```";

        var service = CreateService(
            llmResponse: wrappedResponse,
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        Assert.NotNull(result.GeneratedScenario);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_WithUserGuidance_IncludedInLlmCall()
    {
        var completionClient = new FakeCompletionClient(ValidLlmResponse);
        var service = CreateServiceWithClient(completionClient);

        var request = CreateRequest();
        request.UserGuidance = "Make it a vacation setting";

        await service.AdaptStoryToScenarioAsync(request);

        Assert.Contains("vacation setting", completionClient.LastUserMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_LlmCallIncludesSummary()
    {
        var completionClient = new FakeCompletionClient(ValidLlmResponse);
        var service = CreateServiceWithClient(completionClient);

        await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.Contains("Sarah joins a gym", completionClient.LastUserMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_LlmCallIncludesTargetCharacterNames()
    {
        var completionClient = new FakeCompletionClient(ValidLlmResponse);
        var service = CreateServiceWithClient(completionClient);

        await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.Contains("Becky", completionClient.LastUserMessage!);
        Assert.Contains("Ken", completionClient.LastUserMessage!);
    }

    [Fact]
    public async Task AdaptStoryToScenarioAsync_Style_PopulatedFromResponse()
    {
        var service = CreateService(
            storyDetail: CreateStoryDetail(),
            summary: CreateSummary(),
            analysis: CreateAnalysis(),
            templates: CreateTemplates());

        var result = await service.AdaptStoryToScenarioAsync(CreateRequest());

        Assert.True(result.Success);
        var narrative = result.GeneratedScenario!.Narrative;
        Assert.Equal("Sensual and tension-filled", narrative.NarrativeTone);
        Assert.Equal("Descriptive with internal monologue", narrative.ProseStyle);
        Assert.Equal("Third person limited", narrative.PointOfView);
    }

    private ScenarioAdaptationService CreateServiceWithClient(FakeCompletionClient completionClient)
    {
        return new ScenarioAdaptationService(
            new FakeStoryParserService(CreateStoryDetail()),
            new FakeStoryAnalysisService(CreateAnalysis()),
            new FakeStorySummaryService(CreateSummary()),
            new FakeTemplateService(CreateTemplates()),
            completionClient,
            new FakeModelResolutionService(),
            NullLogger<ScenarioAdaptationService>.Instance);
    }

    #region Test Doubles

    private sealed class FakeStoryParserService(ParsedStoryDetail? detail) : IStoryParserService
    {
        public Task<ParsedStoryDetail?> GetParsedStoryAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult(detail);

        public Task<StoryParseResult> ParseFromUrlAsync(StoryParseRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<bool> PurgeParsedStoryAsync(string id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task<List<DreamGenClone.Domain.StoryParser.ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeStoryAnalysisService(StoryAnalysisResult? analysis) : IStoryAnalysisService
    {
        public Task<StoryAnalysisResult?> GetAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(analysis);

        public Task<AnalyzeResult> AnalyzeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeStorySummaryService(StorySummary? summary) : IStorySummaryService
    {
        public Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(summary);

        public Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeTemplateService(List<TemplateDefinition> templates) : ITemplateService
    {
        public Task<TemplateDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(templates.FirstOrDefault(t => t.Id == id));

        public Task<IReadOnlyList<TemplateDefinition>> GetAllAsync(TemplateType? templateType = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<TemplateDefinition>>(templates.Where(t => templateType == null || t.TemplateType == templateType).ToList());

        public Task<TemplateDefinition> SaveAsync(TemplateDefinition template, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task UpdateImagePathAsync(Guid id, string imagePath, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    internal sealed class FakeCompletionClient(string response) : ICompletionClient
    {
        public string? LastSystemMessage { get; private set; }
        public string? LastUserMessage { get; private set; }

        public Task<string> GenerateAsync(string prompt, ResolvedModel resolved, CancellationToken cancellationToken = default)
            => Task.FromResult(response);

        public Task<string> GenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, CancellationToken cancellationToken = default)
        {
            LastSystemMessage = systemMessage;
            LastUserMessage = userMessage;
            return Task.FromResult(response);
        }

        public async Task<string> StreamGenerateAsync(string prompt, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            await onChunk(response);
            return response;
        }

        public async Task<string> StreamGenerateAsync(string systemMessage, string userMessage, ResolvedModel resolved, Func<string, Task> onChunk, CancellationToken cancellationToken = default)
        {
            LastSystemMessage = systemMessage;
            LastUserMessage = userMessage;
            await onChunk(response);
            return response;
        }

        public Task<bool> CheckHealthAsync(string providerBaseUrl, int timeoutSeconds, string? decryptedApiKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string Message)> CheckModelHealthAsync(string providerBaseUrl, string chatCompletionsPath, int timeoutSeconds, string? decryptedApiKey, string modelIdentifier, CancellationToken cancellationToken = default)
            => Task.FromResult((true, "OK"));
    }

    internal sealed class FakeModelResolutionService : IModelResolutionService
    {
        public Task<ResolvedModel> ResolveAsync(
            AppFunction function,
            string? sessionModelId = null,
            double? sessionTemperature = null,
            double? sessionTopP = null,
            int? sessionMaxTokens = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ResolvedModel(
                ProviderBaseUrl: "http://127.0.0.1:1234",
                ChatCompletionsPath: "/v1/chat/completions",
                ProviderTimeoutSeconds: 120,
                ApiKeyEncrypted: null,
                ModelIdentifier: "test-model",
                Temperature: sessionTemperature ?? 0.7,
                TopP: sessionTopP ?? 0.9,
                MaxTokens: sessionMaxTokens ?? 500,
                ProviderName: "Test Provider",
                IsSessionOverride: sessionModelId != null));
        }
    }

    #endregion
}
