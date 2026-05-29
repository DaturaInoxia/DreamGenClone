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
            Guid.Parse("5f9a5d7b-2b34-4a2f-ae6d-9a8a2d8d1005"),
            "scenario-guidance:infidelity-brief-disappearance:default",
            BuildTemplate(
                "infidelity-brief-disappearance",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildUp"] = "Establish anticipation through suggestive glances, layered subtext, and brief test absences. The transgressor is building toward acting — keep the mood charged but deniable; avoid any direct physical consummation in this phase.",
                    ["Committed"] = "The transgressor is now actively executing planned disappearances. Each beat must include: a plausible excuse for the absence, a brief private encounter, and a composed return to normal social behavior. The partner remains unaware or only mildly concerned. Build anticipation and confidence between disappearances — do not introduce discovery or full exposure in this phase.",
                    ["Approaching"] = "Disappearances are becoming bolder, longer, or more frequent. Near-miss moments — almost caught returning, disheveled appearance, timing pressure — should increase. The partner may begin to notice a pattern. Physical evidence is harder to conceal. Ratchet up the risk and urgency without delivering the peak confrontation or full consummation yet.",
                    ["Climax"] = "Deliver the peak transgression: the most daring disappearance, the longest encounter, or the moment the pattern nearly collapses. Physical and emotional stakes are at their highest. The composed facade is stretched to its limit. Deliver full intensity in the private encounter and the nerve-wracking return.",
                    ["Reset"] = "Shift to aftermath: composure restored, the facade back in place, but the weight of repeated deception visible beneath the surface. Reflective tone — no further escalation. The partner's unawareness (or dawning suspicion) drives the emotional close.",
                    ["Default"] = "Each narrative beat must respect the core constraint: brief private encounters with immediate return to normal social composure. The tension lives in the gap between what the partner sees and what is happening. Anchor all escalation to the disappearance-and-return rhythm."
                },
                ["composed facade after each return", "time-pressure urgency during the encounter", "plausible excuse logistics", "partner's incremental awareness"],
                ["breaking the disappear-and-return rhythm with extended private scenes", "relocating to secluded private spaces that remove all discovery risk", "rushing to discovery or confrontation before Climax"])),
        new(
            Guid.Parse("5f9a5d7b-2b34-4a2f-ae6d-9a8a2d8d1004"),
            "scenario-guidance:threesome-spontaneous-exclusion-v2:default",
            BuildTemplate(
                "threesome-spontaneous-exclusion-v2",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["BuildUp"] = "Establish the husband's anticipation and excitement alongside the wife's initial shyness. Keep the mood curious and hopeful; avoid any direct physical progression.",
                    ["Committed"] = "Begin the threesome scenario with the wife warming up hesitantly. The husband is actively involved. Avoid depicting exclusion or shift of focus yet — ensure all three characters are engaged.",
                    ["Approaching"] = "Gradually let the wife's engagement with the other man deepen while the husband's involvement starts to naturally diminish. Escalate her focus and his fading presence. Do not yet deliver full explicit consummation or complete exclusion — these belong in Climax.",
                    ["Climax"] = "The wife is fully absorbed in the other man, the husband has faded to observer or invisible status. Deliver the emotional and physical peak: her obliviousness to his exclusion, his complex mix of arousal and hurt unfolds in full.",
                    ["Reset"] = "Shift to aftermath: the husband's shock and quiet realization, the wife's gradual awareness of what happened. Reflective tone, no further explicit escalation.",
                    ["Default"] = "Keep the emotional dynamics of the threesome — wife's growing enthusiasm, husband's diminishing role — as the central tension. Every beat should reinforce the spontaneous shift in focus."
                },
                ["wife's gradual shift of attention", "husband's mixed emotional state", "contrast between wife's absorption and husband's exclusion"],
                ["intentional degradation or humiliation framing", "abrupt pivot away from the exclusion dynamic", "husband breaking scene to protest before climax"])),
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
