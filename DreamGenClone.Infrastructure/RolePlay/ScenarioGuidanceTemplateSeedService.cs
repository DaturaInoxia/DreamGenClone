using System.Text.Json;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioGuidanceTemplateSeedService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly SeedDefinition[] SeedTemplates =
    [
        new(
            Guid.Parse("5f9a5d7b-2b34-4a2f-ae6d-9a8a2d8d1001"),
            "scenario-guidance:dominance:default",
            BuildTemplate(
                "dominance",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildUp"] = "Establish command and response cues gradually while keeping authority explicit.",
                    ["Committed"] = "Maintain a clear dominant/submissive frame and tighten consequence language.",
                    ["Approaching"] = "Escalate intensity through direct instruction and emotionally loaded compliance.",
                    ["Climax"] = "Deliver a decisive culmination led by control language and unambiguous submission.",
                    ["Reset"] = "Transition into cooldown while preserving hierarchy continuity.",
                    ["Default"] = "Keep narrative coherence around negotiated authority dynamics."
                },
                ["consent signals", "power contrast", "consistent command tone"],
                ["abrupt scenario pivots", "out-of-frame tenderness that breaks tone"])),
        new(
            Guid.Parse("5f9a5d7b-2b34-4a2f-ae6d-9a8a2d8d1002"),
            "scenario-guidance:forbidden-risk:default",
            BuildTemplate(
                "forbidden-risk",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildUp"] = "Layer secrecy cues and practical constraints to heighten risk.",
                    ["Committed"] = "Anchor each move to stakes of discovery and consequences.",
                    ["Approaching"] = "Increase pressure via near-discovery beats and narrowing options.",
                    ["Climax"] = "Resolve tension at peak risk while preserving internal plausibility.",
                    ["Reset"] = "Show aftermath and recalibration after danger recedes.",
                    ["Default"] = "Keep choices driven by risk, secrecy, and urgency."
                },
                ["stakes clarity", "near-miss beats", "consequence realism"],
                ["risk-free shortcuts", "sudden tone flattening"])),
        new(
            Guid.Parse("5f9a5d7b-2b34-4a2f-ae6d-9a8a2d8d1003"),
            "scenario-guidance:confession:default",
            BuildTemplate(
                "confession",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildUp"] = "Use hesitation and layered subtext before direct disclosure.",
                    ["Committed"] = "Push toward explicit admission with emotional precision.",
                    ["Approaching"] = "Compress emotional distance and increase vulnerability.",
                    ["Climax"] = "Deliver the confession in unmistakable terms and emotional impact.",
                    ["Reset"] = "Shift to processing reactions and trust recalibration.",
                    ["Default"] = "Center emotional truth and interpersonal consequence."
                },
                ["vulnerability", "specific admission language", "reaction beats"],
                ["detached exposition", "premature resolution"]))
    ];

    private readonly ITemplateService _templateService;
    private readonly ILogger<ScenarioGuidanceTemplateSeedService> _logger;

    public ScenarioGuidanceTemplateSeedService(
        ITemplateService templateService,
        ILogger<ScenarioGuidanceTemplateSeedService> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _templateService.GetAllAsync(TemplateType.ScenarioGuidance, cancellationToken);
        var existingNames = existing
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seededCount = 0;
        foreach (var seed in SeedTemplates)
        {
            if (existingNames.Contains(seed.Name))
            {
                continue;
            }

            await _templateService.SaveAsync(new TemplateDefinition
            {
                Id = seed.Id,
                TemplateType = TemplateType.ScenarioGuidance,
                Name = seed.Name,
                Content = seed.Content
            }, cancellationToken);

            seededCount++;
        }

        _logger.LogInformation("Scenario guidance template seed completed: {SeededCount} new entries, {ExistingCount} already present.", seededCount, existing.Count);
    }

    private static string BuildTemplate(
        string scenarioId,
        IReadOnlyDictionary<string, string> phaseGuidance,
        IReadOnlyList<string> emphasisPoints,
        IReadOnlyList<string> avoidancePoints)
    {
        var payload = new ScenarioGuidanceTemplate
        {
            ScenarioId = scenarioId,
            PhaseGuidance = new Dictionary<string, string>(phaseGuidance, StringComparer.OrdinalIgnoreCase),
            EmphasisPoints = [.. emphasisPoints],
            AvoidancePoints = [.. avoidancePoints]
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private sealed record SeedDefinition(Guid Id, string Name, string Content);
}
