using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Domain.ModelManager;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Web.Application.ModelManager;

public sealed class ModelAnalysisService
{
    private readonly ICompletionClient _completionClient;
    private readonly IModelResolutionService _modelResolutionService;
    private readonly ILogger<ModelAnalysisService> _logger;

    public ModelAnalysisService(
        ICompletionClient completionClient,
        IModelResolutionService modelResolutionService,
        ILogger<ModelAnalysisService> logger)
    {
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _logger = logger;
    }

    public async Task<ModelAnalysisResult> AnalyseModelAsync(RegisteredModel model, Provider provider, CancellationToken cancellationToken = default)
    {
        // Prefer the dedicated ModelAnalysis function, fall back to WritingAssistant
        ResolvedModel resolved;
        try
        {
            resolved = await _modelResolutionService.ResolveAsync(AppFunction.ModelAnalysis, cancellationToken: cancellationToken);
        }
        catch (ModelResolutionException)
        {
            try
            {
                resolved = await _modelResolutionService.ResolveAsync(AppFunction.WritingAssistant, cancellationToken: cancellationToken);
            }
            catch (ModelResolutionException)
            {
                throw new InvalidOperationException(
                    "No model is configured for ModelAnalysis or WritingAssistant. Please assign a model to one of these in Function Defaults before using Analyse.");
            }
        }

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(model, provider);

        _logger.LogInformation("Starting model analysis for {ModelName} on {ProviderName}", model.DisplayName, provider.Name);

        // Analysis produces large structured JSON — ensure enough output tokens regardless of function default settings
        var analysisResolved = resolved with { MaxTokens = Math.Max(resolved.MaxTokens, 3000), Temperature = 0.3 };

        var response = await _completionClient.GenerateAsync(systemPrompt, userPrompt, analysisResolved, cancellationToken);

        return ParseAnalysisResponse(response, model, provider);
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are an expert LLM configuration analyst. You are advising for a specific application called DreamGenClone.

            ## CRITICAL: NO ASSUMPTIONS
            You MUST base all recommendations on the FACTS provided. If a piece of information is marked as "Unknown", you MUST:
            - State clearly in your reasoning what data is missing
            - Use conservative/safe defaults for unknown values
            - NEVER write "I'm assuming" or "likely" or "probably" — only state what you KNOW from the provided data
            - If context window is unknown (0), set maxTokens conservatively to 2000 and note that the user should provide the context window size for better recommendations

            ## About the Application
            DreamGenClone is a local-first creative writing and interactive roleplay platform. It uses LLMs for the following functions.

            IMPORTANT: maxTokens is a CEILING — the maximum the model is allowed to generate. It is NOT the typical output length.
            Set maxTokens generously so the model is never cut off mid-output. The model naturally stops when done; a higher ceiling does not force longer outputs.

            Functions (listed with their purpose and recommended maxTokens CEILING):

            1. **RolePlayGeneration** — Real-time interactive roleplay. The LLM plays characters in a scene. Requires creative, in-character prose, dialogue, actions, and scene descriptions. maxTokens ceiling: same as default (these are the primary creative outputs).

            2. **StoryModeGeneration** — Longer narrative generation continuing a story. Similar to roleplay but more literary. maxTokens ceiling: same as default.

            3. **StorySummarize** — Condenses stories into structured summaries. Requires factual accuracy, no hallucination. Needs LOWER temperature than default for precision, but maxTokens ceiling should still be generous (at least 50-75% of default) because the input stories can be very long and complex.

            4. **StoryAnalyze** — Deep analysis of story content (themes, character arcs, pacing, tone). Requires analytical reasoning and structured assessment. Needs SLIGHTLY LOWER temperature than default. maxTokens ceiling: at least 75% of default because detailed analysis can be lengthy.

            5. **StoryRank** — Scores/ranks stories against criteria with consistent numeric scoring. Needs VERY LOW temperature for determinism. maxTokens ceiling: around 25-50% of default (output is shorter but still needs room for scoring rationale).

            6. **ScenarioPreview** — Generates a preview of how a scenario would play out. Creative but constrained. maxTokens ceiling: same as default.

            7. **ScenarioAdapt** — Adapts an existing scenario to new parameters. Needs to follow instructions precisely while being creative. maxTokens ceiling: same as default.

            8. **WritingAssistant** — General-purpose writing help (suggestions, continuations, rewrites). Versatile. maxTokens ceiling: same as default.

            9. **RolePlayAssistant** — Helps users craft roleplay prompts, character sheets, scenario setups. Advisory/instructional. maxTokens ceiling: at least 75% of default.

            10. **ModelAnalysis** — This function itself: analysing models and recommending settings. Needs LOW temperature. maxTokens ceiling: 3000-5000 (structured JSON output).

            ## Rules for the default maxTokens
            The DEFAULT maxTokens (the general baseline) should be set to a useful ceiling for creative generation:
            - If context window is known: default maxTokens = 25% of context window, capped at 8000 (diminishing returns beyond this)
            - If context window is UNKNOWN (0): default maxTokens = 2000 and flag this in reasoning
            - Minimum default maxTokens: 2000 (never recommend less than this as a default)

            ## Rules for function-specific maxTokens
            - Function maxTokens are CEILINGS, not targets. Set them generously.
            - Only provide per-function recommendations where settings MEANINGFULLY differ from the default.
            - DO NOT provide a function recommendation just to set slightly different maxTokens. Only differ if the function needs a genuinely different temperature/topP strategy.
            - If a function uses the same temperature/topP/maxTokens as default, OMIT it from functionRecommendations.

            ## Rules for temperature/sampling
            - Base recommendations on the KNOWN model architecture, parameter count, and quantization
            - Quantized models (Q4, Q5, Q6): lower temperature (0.5-0.8) works better as quantization amplifies sampling noise
            - Full precision models: can handle higher temperature (0.7-1.2) for creative tasks
            - Smaller models (<13B): more constrained settings needed
            - Larger models (70B+): can handle wider creative range

            ## Provider Context
            - **LM Studio (Local)**: Runs quantized models locally. Lower settings generally work better. Slower inference. No API costs.
            - **Together AI (Cloud)**: Runs full-precision models. Can handle higher creativity settings. Fast inference. API cost per token.
            - **OpenRouter (Cloud)**: Gateway to many providers/models. Settings depend on the underlying model. API cost per token.

            ## Your Task
            Given a specific model and provider WITH the provided factual data, produce a comprehensive analysis with:
            1. **Default recommended settings** (temperature, topP, maxTokens) — a good general-purpose baseline for creative generation with this model based on FACTS
            2. **Per-function recommendations for ALL 9 user-facing functions** — you MUST provide a recommendation for every one of these functions: RolePlayGeneration, StoryModeGeneration, StorySummarize, StoryAnalyze, StoryRank, ScenarioPreview, ScenarioAdapt, WritingAssistant, RolePlayAssistant. The user will apply these directly to their configuration, so every function needs explicit values.
            3. **Additional parameters** the model may benefit from (e.g., repetition_penalty, frequency_penalty, min_p, top_k) — only include if relevant and supported
            4. **Capability assessment** — honest evaluation based on KNOWN facts about this model
            5. **Reasoning** explaining your recommendations, citing the specific data points you based them on
            6. **Missing data** — if any critical information was not provided, list it clearly in the reasoning

            ## Response Format
            Return a JSON object with exactly this structure:
            {
              "temperature": <number>,
              "topP": <number>,
              "maxTokens": <integer>,
              "functionRecommendations": [
                {
                  "functionName": "<one of: RolePlayGeneration, StoryModeGeneration, StorySummarize, StoryAnalyze, StoryRank, ScenarioPreview, ScenarioAdapt, WritingAssistant, RolePlayAssistant>",
                  "temperature": <number>,
                  "topP": <number>,
                  "maxTokens": <integer>,
                  "notes": "<brief note about why these values — if same as default, say 'Uses default settings'>"
                }
              ],
              "additionalParameters": {
                "<paramName>": <value>,
                ...
              },
              "modelCapabilityNotes": "<strengths and weaknesses based on KNOWN facts only>",
              "reasoning": "<explanation citing specific data points: context window size, parameter count, quantization, provider type, and any notes provided>"
            }

            You MUST include exactly 9 entries in functionRecommendations — one for each user-facing function listed above.
            Only include additionalParameters that are genuinely useful for this model.
            Return ONLY the JSON object, no markdown fences or extra text.
            """;
    }

    private static string BuildUserPrompt(RegisteredModel model, Provider provider)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Analyse this model for the DreamGenClone application. Use ONLY the facts below. Do NOT assume or infer values that are marked UNKNOWN.");
        sb.AppendLine("Remember: maxTokens is a CEILING (the maximum a model is allowed to generate), not a target. Set ceilings generously so outputs are never truncated.");
        sb.AppendLine();
        sb.AppendLine("## Provider Facts");
        sb.AppendLine($"- Provider Type: {provider.ProviderType}");
        sb.AppendLine($"- Provider Name: {provider.Name}");
        sb.AppendLine($"- Provider Base URL: {provider.BaseUrl}");
        if (!string.IsNullOrWhiteSpace(provider.Notes))
            sb.AppendLine($"- Provider Notes: {provider.Notes}");
        else
            sb.AppendLine("- Provider Notes: (none provided)");
        sb.AppendLine();
        sb.AppendLine("## Model Facts");
        sb.AppendLine($"- Model Identifier: {model.ModelIdentifier}");
        sb.AppendLine($"- Model Display Name: {model.DisplayName}");

        if (model.ContextWindowSize > 0)
            sb.AppendLine($"- Context Window: {model.ContextWindowSize:N0} tokens (KNOWN)");
        else
            sb.AppendLine("- Context Window: UNKNOWN — use conservative maxTokens of 2000 and flag this in reasoning");

        if (!string.IsNullOrWhiteSpace(model.ParameterCount))
            sb.AppendLine($"- Parameter Count: {model.ParameterCount} (KNOWN)");
        else
            sb.AppendLine("- Parameter Count: UNKNOWN — use moderate settings and flag this in reasoning");

        if (!string.IsNullOrWhiteSpace(model.Quantization))
            sb.AppendLine($"- Quantization: {model.Quantization} (KNOWN)");
        else if (provider.ProviderType == ProviderType.LmStudio)
            sb.AppendLine("- Quantization: UNKNOWN (local provider — may be quantized) — flag this in reasoning");
        else
            sb.AppendLine("- Quantization: Not applicable (cloud provider, full precision)");

        if (!string.IsNullOrWhiteSpace(model.Notes))
            sb.AppendLine($"- Model Notes: {model.Notes}");
        else
            sb.AppendLine("- Model Notes: (none provided)");

        sb.AppendLine();
        sb.AppendLine("## Instructions");
        sb.AppendLine("Based ONLY on the KNOWN facts above:");
        sb.AppendLine("1. Set default maxTokens to 25% of the KNOWN context window, capped at 8000, minimum 2000. If context window is UNKNOWN, default to 2000.");
        sb.AppendLine("2. Set temperature/topP based on KNOWN quantization and parameter count. If unknown, use conservative defaults (temp 0.7, topP 0.9).");
        sb.AppendLine("3. Provide recommendations for ALL 9 user-facing functions. If a function should use the same settings as the default, include it anyway with those settings and note 'Uses default settings'.");
        sb.AppendLine("4. Function maxTokens ceilings should be generous — at least 50% of the default for any function. Never recommend maxTokens below 1000 for any function except StoryRank.");
        sb.AppendLine("5. In reasoning, explicitly state which facts drove each recommendation.");
        sb.AppendLine("6. In reasoning, list any UNKNOWN fields that prevented you from making optimal recommendations.");
        return sb.ToString();
    }

    private ModelAnalysisResult ParseAnalysisResponse(string response, RegisteredModel model, Provider provider)
    {
        try
        {
            var jsonText = ExtractJson(response);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<AnalysisJsonResponse>(jsonText, options);

            if (parsed is null)
                throw new JsonException("Parsed result was null");

            var result = new ModelAnalysisResult
            {
                ModelIdentifier = model.ModelIdentifier,
                ProviderType = provider.ProviderType.ToString(),
                SuggestedTemperature = Math.Clamp(parsed.Temperature, 0.0, 2.0),
                SuggestedTopP = Math.Clamp(parsed.TopP, 0.0, 1.0),
                SuggestedMaxTokens = Math.Clamp(parsed.MaxTokens, 50, 8000),
                Reasoning = parsed.Reasoning ?? "No reasoning provided.",
                ModelCapabilityNotes = parsed.ModelCapabilityNotes ?? "",
                AnalysedUtc = DateTime.UtcNow.ToString("o")
            };

            if (parsed.FunctionRecommendations is not null)
            {
                foreach (var rec in parsed.FunctionRecommendations)
                {
                    if (string.IsNullOrWhiteSpace(rec.FunctionName)) continue;
                    result.FunctionRecommendations.Add(new FunctionRecommendation
                    {
                        FunctionName = rec.FunctionName,
                        Temperature = Math.Clamp(rec.Temperature, 0.0, 2.0),
                        TopP = Math.Clamp(rec.TopP, 0.0, 1.0),
                        MaxTokens = Math.Clamp(rec.MaxTokens, 50, 8000),
                        Notes = rec.Notes ?? ""
                    });
                }
            }

            if (parsed.AdditionalParameters is not null)
            {
                foreach (var kvp in parsed.AdditionalParameters)
                {
                    if (kvp.Value is JsonElement element)
                    {
                        result.AdditionalParameters[kvp.Key] = element.ValueKind switch
                        {
                            JsonValueKind.Number => element.TryGetDouble(out var d) ? d : element.GetRawText(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.String => element.GetString() ?? "",
                            _ => element.GetRawText()
                        };
                    }
                    else
                    {
                        result.AdditionalParameters[kvp.Key] = kvp.Value;
                    }
                }
            }

            _logger.LogInformation(
                "Model analysis completed for {ModelName}: Temp={Temperature}, TopP={TopP}, MaxTokens={MaxTokens}, FunctionRecs={FuncRecCount}, AdditionalParams={ParamCount}",
                model.DisplayName, result.SuggestedTemperature, result.SuggestedTopP, result.SuggestedMaxTokens,
                result.FunctionRecommendations.Count, result.AdditionalParameters.Count);

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse analysis response for {ModelName}: {Response}", model.DisplayName, response);
            throw new InvalidOperationException(
                $"The analysis model returned an unparseable response. Raw response: {response[..Math.Min(response.Length, 500)]}");
        }
    }

    /// <summary>Extract the outermost JSON object from a response that may contain markdown fences or preamble text.</summary>
    private static string ExtractJson(string response)
    {
        var text = response.Trim();

        // Strip markdown fences
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0)
                text = text[(firstNewline + 1)..];

            var lastFence = text.LastIndexOf("```");
            if (lastFence >= 0)
                text = text[..lastFence];
        }

        // Find outermost { ... } by brace matching
        var startIdx = text.IndexOf('{');
        if (startIdx < 0)
            return text.Trim();

        var depth = 0;
        for (var i = startIdx; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return text[startIdx..(i + 1)];
            }
        }

        // Braces never balanced — return from first { to end (best effort)
        return text[startIdx..];
    }

    private sealed class AnalysisJsonResponse
    {
        public double Temperature { get; set; }
        public double TopP { get; set; }
        public int MaxTokens { get; set; }
        public List<FunctionRecJson>? FunctionRecommendations { get; set; }
        public Dictionary<string, object>? AdditionalParameters { get; set; }
        public string? ModelCapabilityNotes { get; set; }
        public string? Reasoning { get; set; }
    }

    private sealed class FunctionRecJson
    {
        public string FunctionName { get; set; } = "";
        public double Temperature { get; set; }
        public double TopP { get; set; }
        public int MaxTokens { get; set; }
        public string? Notes { get; set; }
    }
}
