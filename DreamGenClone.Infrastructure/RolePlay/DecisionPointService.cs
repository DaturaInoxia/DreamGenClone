using System.Text.Json;
using System.Text.Json.Serialization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class DecisionPointService : IDecisionPointService
{
    private static readonly IReadOnlyDictionary<string, DecisionOptionTemplate> OptionTemplates =
        new Dictionary<string, DecisionOptionTemplate>(StringComparer.OrdinalIgnoreCase)
        {
            ["lean-in"] = new(
                "lean-in",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 6,
                    ["Tension"] = 4,
                    ["Restraint"] = -20
                },
                WhoTags: ["coworker", "stranger", "friend"],
                WhatTags: ["temptation", "invitation", "flirt"],
                RequiredPhases: [NarrativePhase.Committed, NarrativePhase.Approaching, NarrativePhase.Climax],
                PrerequisitesJson: "{\"min\":{\"Desire\":55}}"),
            ["hold-back"] = new(
                "hold-back",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Restraint"] = 5,
                    ["Tension"] = -2,
                    ["SelfRespect"] = 2
                },
                WhoTags: [],
                WhatTags: [],
                RequiredPhases: [],
                PrerequisitesJson: "{}"),
            ["seek-connection"] = new(
                "seek-connection",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Connection"] = 5,
                    ["Loyalty"] = 3
                },
                WhoTags: ["husband", "partner", "spouse"],
                WhatTags: ["boundary", "trust", "relationship"],
                RequiredPhases: [],
                PrerequisitesJson: "{}"),
            ["test-boundary"] = new(
                "test-boundary",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 5,
                    ["Restraint"] = -20,
                    ["Tension"] = 3
                },
                WhoTags: ["stranger", "coworker"],
                WhatTags: ["temptation", "risk"],
                RequiredPhases: [NarrativePhase.Approaching, NarrativePhase.Climax],
                PrerequisitesJson: "{\"min\":{\"Desire\":65},\"max\":{\"Loyalty\":70}}"),
            ["escalate"] = new(
                "escalate",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Desire"] = 4,
                    ["Tension"] = 4,
                    ["Restraint"] = -25
                },
                WhoTags: [],
                WhatTags: ["temptation", "risk", "invitation", "flirt"],
                RequiredPhases: [NarrativePhase.Committed, NarrativePhase.Approaching, NarrativePhase.Climax],
                PrerequisitesJson: "{}"),
            ["redirect"] = new(
                "redirect",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Restraint"] = 4,
                    ["Connection"] = 2
                },
                WhoTags: [],
                WhatTags: ["invitation", "risk"],
                RequiredPhases: [],
                PrerequisitesJson: "{}"),
            ["observe"] = new(
                "observe",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Tension"] = 1,
                    ["Restraint"] = 2
                },
                WhoTags: [],
                WhatTags: ["risk", "public"],
                RequiredPhases: [],
                PrerequisitesJson: "{}"),
            ["husband-observes"] = new(
                "husband-observes",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                WhoTags: [],
                WhatTags: ["risk", "temptation", "public"],
                RequiredPhases: [NarrativePhase.Approaching, NarrativePhase.Climax],
                PrerequisitesJson: "{\"min\":{\"Desire\":60}}",
                MultiActorDeltas: new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["asking"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Desire"] = 8,
                        ["Restraint"] = -22,
                        ["Loyalty"] = -4,
                        ["Tension"] = 3
                    },
                    ["counterpart"] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Tension"] = 4,
                        ["Connection"] = -2
                    }
                }),
            ["custom"] = new(
                "custom",
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                WhoTags: [],
                WhatTags: [],
                RequiredPhases: [],
                PrerequisitesJson: "{}")
        };

    private readonly ILogger<DecisionPointService> _logger;

    public DecisionPointService(ILogger<DecisionPointService> logger)
    {
        _logger = logger;
    }

    public Task<DecisionPoint?> TryCreateDecisionPointAsync(
        AdaptiveScenarioState state,
        DecisionTrigger trigger,
        DecisionGenerationContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var shouldCreate = trigger == DecisionTrigger.PhaseChanged
            || trigger == DecisionTrigger.SignificantStatChange
            || (trigger == DecisionTrigger.InteractionStart && state.InteractionCountInPhase > 0 && state.InteractionCountInPhase % 3 == 0)
            || trigger == DecisionTrigger.ManualOverride;

        if (!shouldCreate || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return Task.FromResult<DecisionPoint?>(null);
        }

        var decisionPoint = new DecisionPoint
        {
            DecisionPointId = Guid.NewGuid().ToString("N"),
            SessionId = state.SessionId,
            ScenarioId = state.ActiveScenarioId,
            Phase = state.CurrentPhase,
            TriggerSource = trigger.ToString(),
            ContextSummary = context?.PromptSnippet ?? string.Empty,
            AskingActorName = context?.AskingActorName ?? string.Empty,
            TargetActorId = context?.TargetActorId ?? string.Empty,
            TransparencyMode = ResolveTransparencyMode(state, trigger, context),
            OptionIds = BuildContextualOptions(state, context),
            CreatedUtc = DateTime.UtcNow
        };

        return Task.FromResult<DecisionPoint?>(decisionPoint);
    }

    public Task<DecisionOutcome> ApplyDecisionAsync(
        AdaptiveScenarioState state,
        DecisionSubmission submission,
        string? targetActorId = null,
        CancellationToken cancellationToken = default)
    {
        var optionId = submission.OptionId;
        var template = OptionTemplates.TryGetValue(optionId, out var foundTemplate)
            ? foundTemplate
            : null;
        var deltas = string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase)
            ? ParseCustomDeltas(submission.CustomResponseText)
            : template is null
            ? ParseCustomDeltas(submission.CustomResponseText)
            : new Dictionary<string, int>(template.Deltas, StringComparer.OrdinalIgnoreCase);

        var resolvedTargetActorId = ResolveTargetActorId(state, submission, targetActorId);
        var resolvedTargetActor = ResolveActorById(state, resolvedTargetActorId);
        ApplyContextualRestraintDrop(template, deltas, state, resolvedTargetActor);

        if (deltas.Count == 0 && (template?.MultiActorDeltas is null || template.MultiActorDeltas.Count == 0))
        {
            deltas["Connection"] = 1;
        }

        var resolvedAskingActorId = ResolveActorId(state, submission.ActorName);
        var perActorDeltas = template?.MultiActorDeltas is { Count: > 0 }
            ? ApplyDeltasToMappedActors(state, template.MultiActorDeltas, resolvedAskingActorId, resolvedTargetActorId)
            : ApplyDeltasToTarget(state, deltas, resolvedTargetActorId);

        var resolvedTransparency = ResolveTransparencyMode(state, ParseTrigger(submission), null);

        var auditPayload = JsonSerializer.Serialize(new
        {
            submission.ActorName,
            resolvedTargetActorId,
            submission.DecisionPointId,
            optionId,
            appliedAtUtc = DateTime.UtcNow,
            deltas
        });

        _logger.LogInformation(
            RolePlayV2LogEvents.DecisionOutcomeApplied,
            state.SessionId,
            submission.DecisionPointId,
            optionId,
            resolvedTransparency);

        return Task.FromResult(new DecisionOutcome
        {
            Applied = true,
            DecisionPointId = submission.DecisionPointId,
            OptionId = optionId,
            TargetActorId = resolvedTargetActorId,
            AppliedStatDeltas = deltas,
            PerActorStatDeltas = perActorDeltas,
            AuditMetadataJson = auditPayload,
            TransparencyMode = resolvedTransparency,
            Summary = "Decision applied and canonical stat deltas persisted."
        });
    }

    private static List<string> BuildContextualOptions(AdaptiveScenarioState state, DecisionGenerationContext? context)
    {
        var focusActor = ResolveFocusActor(state, context);
        var who = NormalizeContextToken(context?.Who);
        var what = NormalizeContextToken(context?.What);

        var options = OptionTemplates.Values
            .Where(x => !string.Equals(x.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
            .Where(x => IsPhaseMatch(x, state.CurrentPhase))
            .Where(x => IsContextMatch(x, who, what))
            .Where(x => EvaluatePrerequisites(x.PrerequisitesJson, focusActor))
            .OrderByDescending(x => ScoreTemplate(x, focusActor, who, what, state.CurrentPhase, context?.PromptSnippet))
            .Select(x => x.OptionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (options.Count < 2)
        {
            if (!options.Contains("hold-back", StringComparer.OrdinalIgnoreCase))
            {
                options.Add("hold-back");
            }

            if (!options.Contains("seek-connection", StringComparer.OrdinalIgnoreCase))
            {
                options.Add("seek-connection");
            }
        }

        if (!options.Contains("custom", StringComparer.OrdinalIgnoreCase))
        {
            options.Add("custom");
        }

        if (IsEscalationContext(context?.PromptSnippet, what)
            && !options.Contains("escalate", StringComparer.OrdinalIgnoreCase)
            && IsPhaseMatch(OptionTemplates["escalate"], state.CurrentPhase))
        {
            options.Insert(0, "escalate");
        }

        return options;
    }

    private static bool IsPhaseMatch(DecisionOptionTemplate template, NarrativePhase phase)
    {
        return template.RequiredPhases.Count == 0 || template.RequiredPhases.Contains(phase);
    }

    private static bool IsContextMatch(DecisionOptionTemplate template, string? who, string? what)
    {
        var whoMatch = template.WhoTags.Count == 0
            || string.IsNullOrWhiteSpace(who)
            || template.WhoTags.Contains(who, StringComparer.OrdinalIgnoreCase);
        var whatMatch = template.WhatTags.Count == 0
            || string.IsNullOrWhiteSpace(what)
            || template.WhatTags.Contains(what, StringComparer.OrdinalIgnoreCase);
        return whoMatch && whatMatch;
    }

    private static int ScoreTemplate(
        DecisionOptionTemplate template,
        CharacterStatProfileV2? focusActor,
        string? who,
        string? what,
        NarrativePhase phase,
        string? promptSnippet)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(who) && template.WhoTags.Contains(who, StringComparer.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(what) && template.WhatTags.Contains(what, StringComparer.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (template.RequiredPhases.Contains(phase))
        {
            score += 3;
        }

        if (!string.IsNullOrWhiteSpace(promptSnippet)
            && promptSnippet.Contains("risk", StringComparison.OrdinalIgnoreCase)
            && template.WhatTags.Contains("risk", StringComparer.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (string.Equals(template.OptionId, "escalate", StringComparison.OrdinalIgnoreCase)
            && IsEscalationContext(promptSnippet, what))
        {
            score += 6;
        }

        if (focusActor is not null)
        {
            if (focusActor.Desire >= 65 && string.Equals(template.OptionId, "test-boundary", StringComparison.OrdinalIgnoreCase))
            {
                score += 4;
            }

            if (focusActor.Restraint >= 65 && string.Equals(template.OptionId, "hold-back", StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
        }

        return score;
    }

    private static bool IsEscalationContext(string? promptSnippet, string? what)
    {
        if (!string.IsNullOrWhiteSpace(what)
            && (string.Equals(what, "risk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(what, "temptation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(what, "invitation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(what, "flirt", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(promptSnippet))
        {
            return false;
        }

        return promptSnippet.Contains("escalat", StringComparison.OrdinalIgnoreCase)
            || promptSnippet.Contains("tempt", StringComparison.OrdinalIgnoreCase)
            || promptSnippet.Contains("risk", StringComparison.OrdinalIgnoreCase)
            || promptSnippet.Contains("push", StringComparison.OrdinalIgnoreCase);
    }

    private static bool EvaluatePrerequisites(string? prerequisitesJson, CharacterStatProfileV2? actor)
    {
        if (actor is null || string.IsNullOrWhiteSpace(prerequisitesJson) || prerequisitesJson == "{}")
        {
            return true;
        }

        PrerequisiteSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<PrerequisiteSpec>(prerequisitesJson);
        }
        catch
        {
            return false;
        }

        IReadOnlyDictionary<string, double>? formulaScores = null;

        if (spec?.Min is not null)
        {
            foreach (var (statName, value) in spec.Min)
            {
                if (GetStatValue(actor, statName) < value)
                {
                    return false;
                }
            }
        }

        if (spec?.Max is not null)
        {
            foreach (var (statName, value) in spec.Max)
            {
                if (GetStatValue(actor, statName) > value)
                {
                    return false;
                }
            }
        }

        if (spec?.MinFormula is not null)
        {
            formulaScores ??= RolePlayDerivedFormulaEvaluator.EvaluateAll(actor);
            foreach (var (formulaName, value) in spec.MinFormula)
            {
                if (!TryGetFormulaScore(formulaScores, formulaName, out var formulaScore) || formulaScore < value)
                {
                    return false;
                }
            }
        }

        if (spec?.MaxFormula is not null)
        {
            formulaScores ??= RolePlayDerivedFormulaEvaluator.EvaluateAll(actor);
            foreach (var (formulaName, value) in spec.MaxFormula)
            {
                if (!TryGetFormulaScore(formulaScores, formulaName, out var formulaScore) || formulaScore > value)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int GetStatValue(CharacterStatProfileV2 actor, string statName)
    {
        return CharacterStatProfileV2Accessor.GetStatOrDefault(actor, statName, AdaptiveStatCatalog.DefaultValue);
    }

    private static bool TryGetFormulaScore(IReadOnlyDictionary<string, double> formulas, string formulaName, out double score)
    {
        score = 0;
        if (string.IsNullOrWhiteSpace(formulaName))
        {
            return false;
        }

        if (formulas.TryGetValue(formulaName, out score))
        {
            return true;
        }

        var compact = ToComparableKey(formulaName);
        foreach (var (key, value) in formulas)
        {
            if (ToComparableKey(key) == compact)
            {
                score = value;
                return true;
            }
        }

        return false;
    }

    private static string ToComparableKey(string value)
    {
        return new string(value
            .Trim()
            .Where(c => c != '_' && c != '-' && c != ' ')
            .ToArray())
            .ToUpperInvariant();
    }

    private static string? NormalizeContextToken(string? token)
    {
        return string.IsNullOrWhiteSpace(token)
            ? null
            : token.Trim().ToLowerInvariant();
    }

    private static CharacterStatProfileV2? ResolveFocusActor(AdaptiveScenarioState state, DecisionGenerationContext? context)
    {
        if (!string.IsNullOrWhiteSpace(context?.TargetActorId))
        {
            return state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, context.TargetActorId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(context?.AskingActorName))
        {
            return state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, context.AskingActorName, StringComparison.OrdinalIgnoreCase));
        }

        return state.CharacterSnapshots.FirstOrDefault();
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ApplyDeltasToTarget(
        AdaptiveScenarioState state,
        IReadOnlyDictionary<string, int> deltas,
        string? targetActorId)
    {
        var applied = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        var resolvedTargetActorId = targetActorId;
        if (string.IsNullOrWhiteSpace(resolvedTargetActorId))
        {
            resolvedTargetActorId = state.CharacterSnapshots.FirstOrDefault()?.CharacterId;
        }

        var target = state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, resolvedTargetActorId, StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return applied;
        }

        ApplyDeltas(target, deltas);
        applied[target.CharacterId] = new Dictionary<string, int>(deltas, StringComparer.OrdinalIgnoreCase);
        return applied;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> ApplyDeltasToMappedActors(
        AdaptiveScenarioState state,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> mappedDeltas,
        string? askingActorId,
        string? targetActorId)
    {
        var applied = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (actorToken, actorDeltas) in mappedDeltas)
        {
            var target = ResolveActorByToken(state, actorToken, askingActorId, targetActorId);
            if (target is null)
            {
                continue;
            }

            ApplyDeltas(target, actorDeltas);
            applied[target.CharacterId] = new Dictionary<string, int>(actorDeltas, StringComparer.OrdinalIgnoreCase);
        }

        return applied;
    }

    private static CharacterStatProfileV2? ResolveActorByToken(
        AdaptiveScenarioState state,
        string actorToken,
        string? askingActorId,
        string? targetActorId)
    {
        if (string.IsNullOrWhiteSpace(actorToken))
        {
            return null;
        }

        var normalized = actorToken.Trim().ToLowerInvariant();
        if (normalized is "asking" or "actor" or "initiator" or "persona" or "player")
        {
            if (!string.IsNullOrWhiteSpace(askingActorId))
            {
                var asking = state.CharacterSnapshots.FirstOrDefault(x =>
                    string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase));
                if (asking is not null)
                {
                    return asking;
                }
            }

            return state.CharacterSnapshots.FirstOrDefault();
        }

        if (normalized is "target" or "counterpart" or "partner" or "observer")
        {
            if (!string.IsNullOrWhiteSpace(targetActorId))
            {
                var target = state.CharacterSnapshots.FirstOrDefault(x =>
                    string.Equals(x.CharacterId, targetActorId, StringComparison.OrdinalIgnoreCase));
                if (target is not null)
                {
                    return target;
                }
            }

            if (!string.IsNullOrWhiteSpace(askingActorId))
            {
                return state.CharacterSnapshots.FirstOrDefault(x =>
                           !string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase))
                    ?? state.CharacterSnapshots.FirstOrDefault();
            }

            return state.CharacterSnapshots.FirstOrDefault();
        }

        return state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorToken, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveActorId(AdaptiveScenarioState state, string? actorNameOrId)
    {
        if (string.IsNullOrWhiteSpace(actorNameOrId))
        {
            return null;
        }

        var byId = state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorNameOrId, StringComparison.OrdinalIgnoreCase));
        return byId?.CharacterId;
    }

    private static string? ResolveTargetActorId(AdaptiveScenarioState state, DecisionSubmission submission, string? fallbackTargetActorId)
    {
        if (!string.IsNullOrWhiteSpace(submission.TargetActorId))
        {
            return submission.TargetActorId;
        }

        if (!string.IsNullOrWhiteSpace(fallbackTargetActorId))
        {
            return fallbackTargetActorId;
        }

        if (!string.IsNullOrWhiteSpace(submission.ActorName))
        {
            var actorByName = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, submission.ActorName, StringComparison.OrdinalIgnoreCase));
            if (actorByName is not null)
            {
                return actorByName.CharacterId;
            }
        }

        return state.CharacterSnapshots.FirstOrDefault()?.CharacterId;
    }

    private static DecisionTrigger ParseTrigger(DecisionSubmission submission)
    {
        return submission.OptionId switch
        {
            "custom" => DecisionTrigger.ManualOverride,
            _ => DecisionTrigger.SignificantStatChange
        };
    }

    private static TransparencyMode ResolveTransparencyMode(
        AdaptiveScenarioState state,
        DecisionTrigger trigger,
        DecisionGenerationContext? context = null)
    {
        if (context?.TransparencyOverride is not null)
        {
            return context.TransparencyOverride.Value;
        }

        if (trigger == DecisionTrigger.ManualOverride)
        {
            return TransparencyMode.Explicit;
        }

        return state.CurrentPhase switch
        {
            NarrativePhase.BuildUp => TransparencyMode.Hidden,
            NarrativePhase.Committed => TransparencyMode.Directional,
            NarrativePhase.Approaching => TransparencyMode.Directional,
            NarrativePhase.Climax => TransparencyMode.Explicit,
            _ => TransparencyMode.Directional
        };
    }

    private static Dictionary<string, int> ParseCustomDeltas(string? customResponseText)
    {
        var parsed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(customResponseText))
        {
            return parsed;
        }

        foreach (var segment in customResponseText.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                continue;
            }

            if (int.TryParse(parts[1], out var delta))
            {
                parsed[parts[0]] = delta;
            }
        }

        return parsed;
    }

    private static void ApplyContextualRestraintDrop(
        DecisionOptionTemplate? template,
        IDictionary<string, int> deltas,
        AdaptiveScenarioState state,
        CharacterStatProfileV2? targetActor)
    {
        if (template is null || !IsEscalationTemplate(template))
        {
            return;
        }

        var dropMagnitude = ComputeRestraintDropMagnitude(state.CurrentPhase, targetActor);
        if (deltas.TryGetValue("Restraint", out var existing))
        {
            deltas["Restraint"] = Math.Min(existing, -dropMagnitude);
            return;
        }

        deltas["Restraint"] = -dropMagnitude;
    }

    private static bool IsEscalationTemplate(DecisionOptionTemplate template)
    {
        if (string.Equals(template.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(template.OptionId, "escalate", StringComparison.OrdinalIgnoreCase)
            || string.Equals(template.OptionId, "lean-in", StringComparison.OrdinalIgnoreCase)
            || string.Equals(template.OptionId, "test-boundary", StringComparison.OrdinalIgnoreCase)
            || string.Equals(template.OptionId, "husband-observes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return template.WhatTags.Contains("risk", StringComparer.OrdinalIgnoreCase)
            || template.WhatTags.Contains("temptation", StringComparer.OrdinalIgnoreCase)
            || template.WhatTags.Contains("flirt", StringComparer.OrdinalIgnoreCase)
            || template.WhatTags.Contains("invitation", StringComparer.OrdinalIgnoreCase);
    }

    private static int ComputeRestraintDropMagnitude(NarrativePhase phase, CharacterStatProfileV2? actor)
    {
        var restraint = actor?.Restraint ?? 60;
        var (percent, floor) = phase switch
        {
            NarrativePhase.BuildUp => (0.20, 10),
            NarrativePhase.Committed => (0.30, 15),
            NarrativePhase.Approaching => (0.40, 20),
            NarrativePhase.Climax => (0.50, 25),
            _ => (0.30, 15)
        };

        var magnitude = Math.Max(floor, (int)Math.Round(restraint * percent, MidpointRounding.AwayFromZero));

        if (actor is not null)
        {
            if (actor.Desire >= 65)
            {
                magnitude += 2;
            }

            if (actor.Tension >= 60)
            {
                magnitude += 2;
            }
        }

        return Math.Clamp(magnitude, 10, 50);
    }

    private static CharacterStatProfileV2? ResolveActorById(AdaptiveScenarioState state, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return null;
        }

        return state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyDeltas(CharacterStatProfileV2 profile, IReadOnlyDictionary<string, int> deltas)
    {
        foreach (var (stat, delta) in deltas)
        {
            CharacterStatProfileV2Accessor.ApplyDelta(profile, stat, delta);
        }
    }

    private sealed record DecisionOptionTemplate(
        string OptionId,
        IReadOnlyDictionary<string, int> Deltas,
        IReadOnlyList<string> WhoTags,
        IReadOnlyList<string> WhatTags,
        IReadOnlyList<NarrativePhase> RequiredPhases,
        string PrerequisitesJson,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? MultiActorDeltas = null);

    private sealed class PrerequisiteSpec
    {
        [JsonPropertyName("min")]
        public Dictionary<string, int>? Min { get; set; }

        [JsonPropertyName("max")]
        public Dictionary<string, int>? Max { get; set; }

        [JsonPropertyName("minFormula")]
        public Dictionary<string, double>? MinFormula { get; set; }

        [JsonPropertyName("maxFormula")]
        public Dictionary<string, double>? MaxFormula { get; set; }
    }
}
