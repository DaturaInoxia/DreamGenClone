using System.Text;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.Templates;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.Scenarios;

public sealed class ScenarioAdaptationService : IScenarioAdaptationService
{
    private readonly IStoryParserService _storyParserService;
    private readonly IStoryAnalysisService _analysisService;
    private readonly IStorySummaryService _summaryService;
    private readonly ITemplateService _templateService;
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolver;
    private readonly ILogger<ScenarioAdaptationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ScenarioAdaptationService(
        IStoryParserService storyParserService,
        IStoryAnalysisService analysisService,
        IStorySummaryService summaryService,
        ITemplateService templateService,
        ICompletionClient completionClient,
        IModelResolutionService modelResolver,
        ILogger<ScenarioAdaptationService> logger)
    {
        _storyParserService = storyParserService;
        _analysisService = analysisService;
        _summaryService = summaryService;
        _templateService = templateService;
        _completionClient = completionClient;
        _modelResolver = modelResolver;
        _logger = logger;
    }

    public async Task<ScenarioPreviewResult> PreviewScenarioAsync(
        string parsedStoryId,
        CancellationToken cancellationToken = default)
    {
        // 1. Load summary (required)
        var summary = await _summaryService.GetSummaryAsync(parsedStoryId, cancellationToken);
        if (summary is null || string.IsNullOrWhiteSpace(summary.SummaryText))
        {
            return new ScenarioPreviewResult
            {
                Success = false,
                ErrorMessage = "Story must be summarized before preview. Please run Summarize first."
            };
        }

        // 2. Load analysis (optional — enriches preview)
        var analysis = await _analysisService.GetAnalysisAsync(parsedStoryId, cancellationToken);

        // 3. Build preview prompt and call LLM with unrestricted model
        var systemMessage = BuildPreviewSystemMessage();
        var userMessage = BuildPreviewUserMessage(summary.SummaryText, analysis);

        _logger.LogInformation(
            "Generating scenario preview for parsed story '{StoryId}'.", parsedStoryId);

        string llmResponse;
        try
        {
            var previewResolved = await _modelResolver.ResolveAsync(AppFunction.ScenarioPreview, cancellationToken: cancellationToken);
            llmResponse = await _completionClient.GenerateAsync(
                systemMessage,
                userMessage,
                previewResolved,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during scenario preview for story '{StoryId}'.", parsedStoryId);
            return new ScenarioPreviewResult
            {
                Success = false,
                ErrorMessage = $"LLM generation failed: {ex.Message}"
            };
        }

        // 4. Parse preview response
        return ParsePreviewResponse(llmResponse);
    }

    public async Task<AdaptStoryResult> BuildScenarioFromPreviewAsync(
        ScenarioPreviewResult preview,
        List<CharacterSubstitution> characterSubstitutions,
        string? sourceStoryId,
        CancellationToken cancellationToken = default)
    {
        // 1. Load character templates
        var characters = new List<Character>();
        foreach (var sub in characterSubstitutions)
        {
            var template = await _templateService.GetByIdAsync(sub.TemplateId, cancellationToken);
            if (template is null)
            {
                _logger.LogWarning("Character template {TemplateId} not found, skipping.", sub.TemplateId);
                continue;
            }
            characters.Add(new Character
            {
                Name = template.Name,
                Description = template.Content,
                Role = sub.TargetRole,
                TemplateId = template.Id.ToString()
            });
        }

        if (characters.Count == 0)
        {
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = "At least one valid character template is required."
            };
        }

        // 2. Build scenario directly from the edited preview
        var scenario = new Scenario
        {
            Name = preview.PlotTitle ?? "Adapted Scenario",
            Plot = new Plot
            {
                Title = preview.PlotTitle,
                Description = preview.PlotDescription,
                Conflicts = preview.Conflicts.Where(c => !string.IsNullOrWhiteSpace(c)).ToList(),
                Goals = preview.Goals.Where(g => !string.IsNullOrWhiteSpace(g)).ToList()
            },
            Setting = new Setting
            {
                WorldDescription = preview.SettingSummary
            },
            Style = new Style
            {
                Tone = preview.StyleSummary
            },
            Characters = characters
        };

        // 3. Set provenance
        string? sourceTitle = null;
        if (!string.IsNullOrWhiteSpace(sourceStoryId))
        {
            var storyDetail = await _storyParserService.GetParsedStoryAsync(sourceStoryId, cancellationToken);
            sourceTitle = storyDetail?.Title;
            var provenanceNote = $"Adapted from: {sourceTitle ?? "Unknown Title"}";
            scenario.Description = string.IsNullOrWhiteSpace(scenario.Plot.Description)
                ? provenanceNote
                : $"{scenario.Plot.Description}\n\n{provenanceNote}";
        }

        _logger.LogInformation(
            "Built scenario '{ScenarioName}' from preview with {CharacterCount} characters.",
            scenario.Name, characters.Count);

        return new AdaptStoryResult
        {
            Success = true,
            GeneratedScenario = scenario,
            SourceParsedStoryId = sourceStoryId,
            SourceStoryTitle = sourceTitle
        };
    }

    public async Task<AdaptStoryResult> AdaptStoryToScenarioAsync(
        AdaptStoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Load parsed story metadata
        var storyDetail = await _storyParserService.GetParsedStoryAsync(request.ParsedStoryId, cancellationToken);
        if (storyDetail is null)
        {
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = $"Parsed story '{request.ParsedStoryId}' not found."
            };
        }

        // 2. Load summary (required)
        var summary = await _summaryService.GetSummaryAsync(request.ParsedStoryId, cancellationToken);
        if (summary is null || string.IsNullOrWhiteSpace(summary.SummaryText))
        {
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = "Story must be summarized before adaptation. Please run Summarize first."
            };
        }

        // 3. Load analysis (optional — enriches the adaptation but not strictly required)
        var analysis = await _analysisService.GetAnalysisAsync(request.ParsedStoryId, cancellationToken);

        // 4. Load character templates for substitution
        var substitutionCharacters = new List<(TemplateDefinition Template, string? TargetRole)>();
        foreach (var sub in request.CharacterSubstitutions)
        {
            var template = await _templateService.GetByIdAsync(sub.TemplateId, cancellationToken);
            if (template is null)
            {
                _logger.LogWarning("Character template {TemplateId} not found, skipping.", sub.TemplateId);
                continue;
            }
            substitutionCharacters.Add((template, sub.TargetRole));
        }

        if (substitutionCharacters.Count == 0)
        {
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = "At least one valid character template is required for substitution."
            };
        }

        // 5. Build prompt and call LLM
        var systemMessage = BuildSystemMessage();
        var userMessage = BuildUserMessage(summary.SummaryText, analysis, substitutionCharacters, request.UserGuidance);

        _logger.LogInformation(
            "Adapting parsed story '{StoryId}' ({Title}) to scenario with {CharacterCount} substitution characters.",
            request.ParsedStoryId, storyDetail.Title, substitutionCharacters.Count);

        string llmResponse;
        try
        {
            var adaptResolved = await _modelResolver.ResolveAsync(AppFunction.ScenarioAdapt, cancellationToken: cancellationToken);
            llmResponse = await _completionClient.GenerateAsync(
                systemMessage,
                userMessage,
                adaptResolved,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM call failed during scenario adaptation for story '{StoryId}'.", request.ParsedStoryId);
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = $"LLM generation failed: {ex.Message}"
            };
        }

        // 6. Parse LLM response into Scenario
        var (scenario, mappings, notes) = ParseLlmResponse(llmResponse, substitutionCharacters);
        if (scenario is null)
        {
            return new AdaptStoryResult
            {
                Success = false,
                ErrorMessage = "Failed to parse LLM response into a valid scenario. Raw response logged.",
                AdaptationNotes = llmResponse
            };
        }

        // Set provenance in description
        var provenanceNote = $"Adapted from: {storyDetail.Title ?? "Unknown Title"}";
        scenario.Description = string.IsNullOrWhiteSpace(scenario.Description)
            ? provenanceNote
            : $"{scenario.Description}\n\n{provenanceNote}";

        return new AdaptStoryResult
        {
            Success = true,
            GeneratedScenario = scenario,
            SourceParsedStoryId = request.ParsedStoryId,
            SourceStoryTitle = storyDetail.Title,
            CharacterMappings = mappings,
            AdaptationNotes = notes
        };
    }

    private static string BuildSystemMessage()
    {
        return """
            You are a scenario adaptation assistant. Your job is to transform a story's analysis into a roleplay/story scenario.

            You must follow a two-stage process:
            1. DISTILL THE PREMISE: Extract a character-agnostic scenario concept from the story. This is the situational premise and core tension — NO character names, 2-4 sentences. Think of it as a scenario card title and description (e.g., "Fling with Fitness Trainer — A married woman begins private sessions with a charismatic personal trainer. The workouts become increasingly hands-on...").
            2. BUILD THE SCENARIO: Cast the provided target characters into the roles, generate setting, style, opening, and locations.

            CRITICAL RULES:
            - Plot.title must be a short, evocative scenario name (3-8 words)
            - Plot.description must be character-agnostic: NO character names, just the situation, tension, and setup (2-4 sentences)
            - Characters appear ONLY in the characters array and opening text
            - The opening text IS character-specific and should set the scene with the target characters

            Respond ONLY with valid JSON in this exact format (no text outside the JSON):
            {
              "plot": {
                "title": "Short Evocative Title",
                "description": "Character-agnostic premise. 2-4 sentences describing the situation, tension, and setup without any names.",
                "conflicts": ["conflict 1", "conflict 2"],
                "goals": ["goal 1", "goal 2"]
              },
              "setting": {
                "worldDescription": "Description of the physical world and environment",
                "timeFrame": "When this takes place",
                "environmentalDetails": ["detail 1"],
                "worldRules": ["rule 1"]
              },
              "style": {
                "tone": "The emotional tone",
                "writingStyle": "The narrative writing style",
                "pointOfView": "Narrative perspective",
                "styleGuidelines": ["guideline 1"]
              },
              "characters": [
                {
                  "name": "Character Name",
                  "description": "Character description adapted for this scenario",
                  "role": "Their role in this scenario"
                }
              ],
              "locations": [
                { "name": "Location Name", "description": "Location description" }
              ],
              "openings": [
                { "title": "Opening Title", "text": "Scene-starting text featuring the target characters by name" }
              ],
              "characterMappings": [
                { "originalName": "Original Story Character", "substitutedName": "Target Character Name", "role": "their role" }
              ],
              "adaptationNotes": "Brief explanation of how you adapted the story concept and mapped characters"
            }
            """;
    }

    private static string BuildUserMessage(
        string summaryText,
        DreamGenClone.Domain.StoryAnalysis.StoryAnalysisResult? analysis,
        List<(TemplateDefinition Template, string? TargetRole)> substitutionCharacters,
        string? userGuidance)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Story Summary");
        sb.AppendLine(summaryText);
        sb.AppendLine();

        if (analysis is not null)
        {
            if (!string.IsNullOrWhiteSpace(analysis.ThemesJson))
            {
                sb.AppendLine("## Themes");
                sb.AppendLine(analysis.ThemesJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.PlotStructureJson))
            {
                sb.AppendLine("## Plot Structure");
                sb.AppendLine(analysis.PlotStructureJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.WritingStyleJson))
            {
                sb.AppendLine("## Writing Style");
                sb.AppendLine(analysis.WritingStyleJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.CharactersJson))
            {
                sb.AppendLine("## Original Story Characters (for role-mapping context only — these will be REPLACED)");
                sb.AppendLine(analysis.CharactersJson);
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Target Characters (use these in the scenario)");
        foreach (var (template, targetRole) in substitutionCharacters)
        {
            sb.AppendLine($"### {template.Name}");
            if (!string.IsNullOrWhiteSpace(targetRole))
            {
                sb.AppendLine($"Suggested role: {targetRole}");
            }
            sb.AppendLine(template.Content);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(userGuidance))
        {
            sb.AppendLine("## Additional Guidance");
            sb.AppendLine(userGuidance);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private (Scenario? Scenario, List<CharacterMapping> Mappings, string? Notes) ParseLlmResponse(
        string llmResponse,
        List<(TemplateDefinition Template, string? TargetRole)> substitutionCharacters)
    {
        try
        {
            // Strip markdown code fences if present
            var json = llmResponse.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline >= 0)
                    json = json[(firstNewline + 1)..];
                if (json.EndsWith("```"))
                    json = json[..^3];
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var scenario = new Scenario
            {
                Name = GetStringOrDefault(root, "plot", "title", "Adapted Scenario")
            };

            // Plot
            if (root.TryGetProperty("plot", out var plotEl))
            {
                scenario.Plot = new Plot
                {
                    Title = GetString(plotEl, "title"),
                    Description = GetString(plotEl, "description"),
                    Conflicts = GetStringArray(plotEl, "conflicts"),
                    Goals = GetStringArray(plotEl, "goals")
                };
            }

            // Setting
            if (root.TryGetProperty("setting", out var settingEl))
            {
                scenario.Setting = new Setting
                {
                    WorldDescription = GetString(settingEl, "worldDescription"),
                    TimeFrame = GetString(settingEl, "timeFrame"),
                    EnvironmentalDetails = GetStringArray(settingEl, "environmentalDetails"),
                    WorldRules = GetStringArray(settingEl, "worldRules")
                };
            }

            // Style
            if (root.TryGetProperty("style", out var styleEl))
            {
                scenario.Style = new Style
                {
                    Tone = GetString(styleEl, "tone"),
                    WritingStyle = GetString(styleEl, "writingStyle"),
                    PointOfView = GetString(styleEl, "pointOfView"),
                    StyleGuidelines = GetStringArray(styleEl, "styleGuidelines")
                };
            }

            // Characters — link to templates where possible
            if (root.TryGetProperty("characters", out var charsEl) && charsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var charEl in charsEl.EnumerateArray())
                {
                    var charName = GetString(charEl, "name") ?? "Unknown";
                    var character = new Character
                    {
                        Name = charName,
                        Description = GetString(charEl, "description"),
                        Role = GetString(charEl, "role")
                    };

                    // Link to template if this character matches a substitution target
                    var matchingTemplate = substitutionCharacters
                        .FirstOrDefault(sc => string.Equals(sc.Template.Name, charName, StringComparison.OrdinalIgnoreCase));
                    if (matchingTemplate.Template is not null)
                    {
                        character.TemplateId = matchingTemplate.Template.Id.ToString();
                    }

                    scenario.Characters.Add(character);
                }
            }

            // Locations
            if (root.TryGetProperty("locations", out var locsEl) && locsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var locEl in locsEl.EnumerateArray())
                {
                    scenario.Locations.Add(new Location
                    {
                        Name = GetString(locEl, "name"),
                        Description = GetString(locEl, "description")
                    });
                }
            }

            // Openings
            if (root.TryGetProperty("openings", out var openingsEl) && openingsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var openingEl in openingsEl.EnumerateArray())
                {
                    scenario.Openings.Add(new Opening
                    {
                        Title = GetString(openingEl, "title"),
                        Text = GetString(openingEl, "text")
                    });
                }
            }

            // Character mappings
            var mappings = new List<CharacterMapping>();
            if (root.TryGetProperty("characterMappings", out var mappingsEl) && mappingsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var mapEl in mappingsEl.EnumerateArray())
                {
                    mappings.Add(new CharacterMapping
                    {
                        OriginalName = GetString(mapEl, "originalName") ?? string.Empty,
                        SubstitutedName = GetString(mapEl, "substitutedName") ?? string.Empty,
                        Role = GetString(mapEl, "role")
                    });
                }
            }

            // Adaptation notes
            var notes = GetString(root, "adaptationNotes");

            return (scenario, mappings, notes);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM scenario adaptation response as JSON.");
            return (null, [], null);
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static string GetStringOrDefault(JsonElement root, string parentProperty, string childProperty, string defaultValue)
    {
        if (root.TryGetProperty(parentProperty, out var parent) &&
            parent.TryGetProperty(childProperty, out var child) &&
            child.ValueKind == JsonValueKind.String)
        {
            return child.GetString() ?? defaultValue;
        }
        return defaultValue;
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var val = item.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    result.Add(val);
            }
        }
        return result;
    }

    private static string BuildPreviewSystemMessage()
    {
        return """
            You are a scenario concept generator. Your job is to distill a story into a character-agnostic scenario concept that can later be adapted with specific characters.

            Extract the core premise, tension, setting, and style from the story — but do NOT include any character names. Think of this as creating a "scenario card" that describes the situation anyone could step into.

            CRITICAL RULES:
            - Plot title: short, evocative scenario name (3-8 words)
            - Plot description: character-agnostic, NO names — just the situation, tension, and setup (2-4 sentences)
            - Setting: describe the world and environment
            - Style: capture the tone, writing style, and narrative perspective
            - Suggested roles: list the character roles needed for this scenario (e.g., "protagonist", "love interest", "antagonist")

            Respond ONLY with valid JSON in this exact format (no text outside the JSON):
            {
              "plot": {
                "title": "Short Evocative Title",
                "description": "Character-agnostic premise. 2-4 sentences describing the situation, tension, and setup without any names.",
                "conflicts": ["conflict 1", "conflict 2"],
                "goals": ["goal 1", "goal 2"]
              },
              "setting": {
                "summary": "Brief description of the world, location, and timeframe"
              },
              "style": {
                "summary": "Brief description of the tone, writing style, and narrative perspective"
              },
              "suggestedRoles": ["role 1", "role 2"]
            }
            """;
    }

    private static string BuildPreviewUserMessage(
        string summaryText,
        DreamGenClone.Domain.StoryAnalysis.StoryAnalysisResult? analysis)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Story Summary");
        sb.AppendLine(summaryText);
        sb.AppendLine();

        if (analysis is not null)
        {
            if (!string.IsNullOrWhiteSpace(analysis.ThemesJson))
            {
                sb.AppendLine("## Themes");
                sb.AppendLine(analysis.ThemesJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.PlotStructureJson))
            {
                sb.AppendLine("## Plot Structure");
                sb.AppendLine(analysis.PlotStructureJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.WritingStyleJson))
            {
                sb.AppendLine("## Writing Style");
                sb.AppendLine(analysis.WritingStyleJson);
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(analysis.CharactersJson))
            {
                sb.AppendLine("## Original Story Characters (for role identification only)");
                sb.AppendLine(analysis.CharactersJson);
                sb.AppendLine();
            }
        }

        sb.AppendLine("Distill this story into a character-agnostic scenario concept. Extract the premise, setting, style, and identify the character roles needed.");

        return sb.ToString();
    }

    private ScenarioPreviewResult ParsePreviewResponse(string llmResponse)
    {
        try
        {
            var json = llmResponse.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                if (firstNewline >= 0)
                    json = json[(firstNewline + 1)..];
                if (json.EndsWith("```"))
                    json = json[..^3];
                json = json.Trim();
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new ScenarioPreviewResult { Success = true };

            if (root.TryGetProperty("plot", out var plotEl))
            {
                result.PlotTitle = GetString(plotEl, "title");
                result.PlotDescription = GetString(plotEl, "description");
                result.Conflicts = GetStringArray(plotEl, "conflicts");
                result.Goals = GetStringArray(plotEl, "goals");
            }

            if (root.TryGetProperty("setting", out var settingEl))
            {
                result.SettingSummary = GetString(settingEl, "summary");
            }

            if (root.TryGetProperty("style", out var styleEl))
            {
                result.StyleSummary = GetString(styleEl, "summary");
            }

            result.SuggestedRoles = GetStringArray(root, "suggestedRoles");

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM scenario preview response as JSON.");
            return new ScenarioPreviewResult
            {
                Success = false,
                ErrorMessage = "Failed to parse LLM preview response. Raw response logged."
            };
        }
    }
}
