using System.Text.Json;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.Templates;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class ScenarioGuidanceGenerator : IScenarioGuidanceGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ITemplateService _templateService;
    private readonly IStatWillingnessProfileService? _statWillingnessProfileService;
    private readonly IHusbandAwarenessProfileService? _husbandAwarenessProfileService;
    private readonly ILogger<ScenarioGuidanceGenerator> _logger;

    public ScenarioGuidanceGenerator(
        ITemplateService templateService,
        ILogger<ScenarioGuidanceGenerator> logger,
        IStatWillingnessProfileService? statWillingnessProfileService = null,
        IHusbandAwarenessProfileService? husbandAwarenessProfileService = null)
    {
        _templateService = templateService;
        _logger = logger;
        _statWillingnessProfileService = statWillingnessProfileService;
        _husbandAwarenessProfileService = husbandAwarenessProfileService;
    }

    public async Task<ScenarioGuidanceOutput> GenerateGuidanceAsync(
        ScenarioGuidanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var template = await ResolveTemplateAsync(request, cancellationToken);
        if (template is null)
        {
            return new ScenarioGuidanceOutput
            {
                GuidanceText = BuildFallbackGuidance(request.CurrentPhase, request.ActiveScenarioId),
                Source = "Fallback"
            };
        }

        var phaseGuidance = ResolvePhaseGuidance(template, request.CurrentPhase);
        var statInterpretation = BuildStatInterpretation(
            request.AverageDesire,
            request.AverageRestraint,
            request.AverageTension,
            request.AverageConnection,
            request.AverageDominance,
            request.AverageLoyalty);
        var guidanceText = string.IsNullOrWhiteSpace(statInterpretation)
            ? phaseGuidance
            : $"{phaseGuidance} {statInterpretation}";

        var willingnessInterpretation = await BuildWillingnessInterpretationAsync(request, cancellationToken);
        if (!string.IsNullOrWhiteSpace(willingnessInterpretation))
        {
            guidanceText = $"{guidanceText} {willingnessInterpretation}";
        }

        var husbandAwarenessFrame = await BuildHusbandAwarenessInterpretationAsync(request, cancellationToken);

        return new ScenarioGuidanceOutput
        {
            GuidanceText = guidanceText,
            HusbandAwarenessFrame = husbandAwarenessFrame,
            EmphasisPoints = template.EmphasisPoints,
            AvoidancePoints = template.AvoidancePoints,
            Source = $"Template:{template.ScenarioId}"
        };
    }

    private async Task<ScenarioGuidanceTemplate?> ResolveTemplateAsync(
        ScenarioGuidanceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ActiveScenarioId))
        {
            return null;
        }

        var templates = await _templateService.GetAllAsync(TemplateType.ScenarioGuidance, cancellationToken);
        if (templates.Count == 0)
        {
            return null;
        }

        foreach (var template in templates)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ScenarioGuidanceTemplate>(template.Content, JsonOptions);
                if (payload is null)
                {
                    continue;
                }

                if (!string.Equals(payload.ScenarioId, request.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(request.VariantId)
                    && !string.IsNullOrWhiteSpace(payload.VariantId)
                    && !string.Equals(payload.VariantId, request.VariantId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return payload;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse scenario guidance template {TemplateId}", template.Id);
            }
        }

        return null;
    }

    private static string ResolvePhaseGuidance(ScenarioGuidanceTemplate template, string currentPhase)
    {
        if (template.PhaseGuidance.TryGetValue(currentPhase, out var direct) && !string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (template.PhaseGuidance.TryGetValue("Default", out var fallback) && !string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        return BuildFallbackGuidance(currentPhase, template.ScenarioId);
    }

    private static string BuildStatInterpretation(
        double averageDesire,
        double averageRestraint,
        double averageTension,
        double averageConnection,
        double averageDominance,
        double averageLoyalty)
    {
        var notes = new List<string>();

        if (averageDesire >= 70)
        {
            notes.Add("High desire suggests proactive pursuit by involved characters.");
        }
        else if (averageDesire <= 35)
        {
            notes.Add("Lower desire suggests slower pacing and stronger motivational setup.");
        }

        if (averageRestraint >= 70)
        {
            notes.Add("High restraint implies composed behavior and guarded choices.");
        }
        else if (averageRestraint <= 35)
        {
            notes.Add("Low restraint supports impulse-driven escalation.");
        }

        if (averageConnection >= 70)
        {
            notes.Add("High connection supports trust-forward interactions and emotional openness.");
        }
        else if (averageConnection <= 35)
        {
            notes.Add("Low connection favors distance-first framing and reduced vulnerability.");
        }

        if (averageTension >= 70)
        {
            notes.Add("High tension permits more risk-tinged pacing and volatile scene turns.");
        }
        else if (averageTension <= 35)
        {
            notes.Add("Low tension supports steadier pacing with lower-risk scene framing.");
        }

        var agencyScore = averageDominance - averageRestraint + (averageDesire / 3.0);
        if (agencyScore >= 60)
        {
            notes.Add("Agency profile is proactive: the actor can initiate and direct escalation.");
        }
        else if (agencyScore >= 30)
        {
            notes.Add("Agency profile is opportunistic: the actor responds readily to invitation and momentum.");
        }
        else
        {
            notes.Add("Agency profile is reactive: stronger external invitation is needed before escalation.");
        }

        var cheatingPressure = averageLoyalty - (averageDesire / 2.0) + (averageRestraint / 2.0) - (averageTension / 3.0);
        if (cheatingPressure >= 80)
        {
            notes.Add("Loyalty pressure is high, so keep decisions spouse-anchored and avoid infidelity pivots.");
        }
        else if (cheatingPressure >= 60)
        {
            notes.Add("Loyalty pressure is moderate-high; require stronger trust and setup before boundary-crossing choices.");
        }
        else if (cheatingPressure >= 40)
        {
            notes.Add("Loyalty pressure is mixed; use ambivalence and conditional decision framing.");
        }
        else
        {
            notes.Add("Loyalty pressure is low, so transgressive choices can emerge with less resistance.");
        }

        return string.Join(" ", notes);
    }

    private static string BuildFallbackGuidance(string phase, string? scenarioLabel)
    {
        var label = string.IsNullOrWhiteSpace(scenarioLabel) ? "current narrative direction" : scenarioLabel;

        return phase switch
        {
            "BuildUp" => "Use subtle, exploratory cues and avoid hard commitment language.",
            "Committed" => $"Keep narrative choices anchored to '{label}' and avoid introducing conflicting pivots.",
            "Approaching" => $"Increase anticipation and intensity while preserving coherence with '{label}'.",
            "Climax" => $"Deliver a high-intensity culmination explicitly framed around '{label}'.",
            "Reset" => "Transition to reflective tone and prepare for next build-up.",
            _ => "Maintain coherent narrative progression."
        };
    }

    private async Task<string> BuildWillingnessInterpretationAsync(ScenarioGuidanceRequest request, CancellationToken cancellationToken)
    {
        if (_statWillingnessProfileService is null || string.IsNullOrWhiteSpace(request.SelectedWillingnessProfileId))
        {
            return string.Empty;
        }

        var profile = await _statWillingnessProfileService.GetAsync(request.SelectedWillingnessProfileId, cancellationToken);
        if (profile is null || profile.Thresholds.Count == 0)
        {
            return string.Empty;
        }

        var targetStat = string.IsNullOrWhiteSpace(profile.TargetStatName)
            ? "Desire"
            : profile.TargetStatName.Trim();
        var statValue = ResolveAverageForStat(request, targetStat);
        var roundedDesire = (int)Math.Round(statValue, MidpointRounding.AwayFromZero);
        var threshold = profile.Thresholds
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MinValue)
            .FirstOrDefault(x => roundedDesire >= x.MinValue && roundedDesire <= x.MaxValue);

        if (threshold is null)
        {
            return string.Empty;
        }

        var examples = threshold.ExampleScenarios.Count == 0
            ? string.Empty
            : $" Examples: {string.Join(", ", threshold.ExampleScenarios)}.";

        return $"Willingness band '{threshold.ExplicitnessLevel}' ({threshold.MinValue}-{threshold.MaxValue}) from {targetStat}={roundedDesire}: {threshold.PromptGuideline}.{examples}";
    }

    private static double ResolveAverageForStat(ScenarioGuidanceRequest request, string statName)
    {
        if (string.Equals(statName, "Desire", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageDesire;
        }

        if (string.Equals(statName, "Restraint", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageRestraint;
        }

        if (string.Equals(statName, "Tension", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageTension;
        }

        if (string.Equals(statName, "Connection", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageConnection;
        }

        if (string.Equals(statName, "Dominance", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageDominance;
        }

        if (string.Equals(statName, "Loyalty", StringComparison.OrdinalIgnoreCase))
        {
            return request.AverageLoyalty;
        }

        return request.AverageDesire;
    }

    private async Task<string> BuildHusbandAwarenessInterpretationAsync(ScenarioGuidanceRequest request, CancellationToken cancellationToken)
    {
        if (_husbandAwarenessProfileService is null || string.IsNullOrWhiteSpace(request.HusbandAwarenessProfileId))
        {
            return string.Empty;
        }

        var profile = await _husbandAwarenessProfileService.GetAsync(request.HusbandAwarenessProfileId, cancellationToken);
        if (profile is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(profile.Notes))
        {
            return $"Partner/husband behavioral frame: {profile.Notes.Trim()}";
        }

        // Notes not set — fall back to attribute-derived text.
        var sb = new System.Text.StringBuilder();
        sb.Append("Partner/husband behavioral frame:");

        sb.Append(profile.AwarenessLevel switch
        {
            <= 20 => " The partner has no idea anything is happening; do not write them as suspicious or aware.",
            <= 50 => " The partner senses something may be off but avoids confrontation and does not investigate.",
            <= 75 => " The partner is aware something may be occurring but has chosen not to intervene.",
            _     => " The partner is fully aware of the encounter and has chosen to observe — not confront, not interrupt."
        });

        sb.Append(profile.AcceptanceLevel switch
        {
            <= 20 => " They do not accept this and would react with visible anger if directly confronted.",
            <= 50 => " They feel conflicted and uncomfortable but are holding back their reaction.",
            <= 75 => " They accept what is happening even if uneasy, and will not act to stop it.",
            _     => " They are fully at ease with the encounter continuing."
        });

        sb.Append(profile.VoyeurismLevel switch
        {
            <= 20 => " They have no desire to watch or listen — they stay away and avert attention.",
            <= 50 => " They are mildly curious but resist the urge to watch or listen closely.",
            <= 75 => " They find themselves wanting to observe and may quietly position for a better view.",
            _     => " They actively want to watch and listen; they will deliberately position themselves to observe and will NOT interrupt the encounter."
        });

        sb.Append(profile.ParticipationLevel switch
        {
            <= 20 => " They will not participate in any way.",
            <= 50 => " They might participate briefly only if explicitly and directly invited.",
            <= 75 => " They may seek limited participation if an opening arises.",
            _     => " They actively want to join and may initiate direct participation."
        });

        sb.Append(profile.EncouragementLevel switch
        {
            <= 20 => " They provide no sign of approval or encouragement.",
            <= 50 => " They are passively tolerant — no active encouragement.",
            <= 75 => " They may signal tacit approval through body language or brief remarks.",
            _     => " They openly encourage and facilitate the encounter."
        });

        sb.Append(profile.RiskTolerance switch
        {
            <= 20 => " Any sign of exposure would cause them to immediately retreat or shut everything down.",
            <= 50 => " They strongly prefer discretion and will back away if exposure risk rises.",
            <= 75 => " They accept moderate risk of the encounter being noticed.",
            _     => " They are comfortable with significant exposure risk."
        });

        return sb.ToString();
    }
}
