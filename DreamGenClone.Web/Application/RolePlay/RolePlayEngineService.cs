using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Domain.RolePlay;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Application.Templates;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.StoryAnalysis;
using NarrativePhase = DreamGenClone.Domain.RolePlay.NarrativePhase;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.BackgroundJobs;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.RegularExpressions;

namespace DreamGenClone.Web.Application.RolePlay;

public sealed class RolePlayEngineService : IRolePlayEngineService
{
    private static readonly TimeSpan DecisionPointContextCooldown = TimeSpan.FromMinutes(5);
    private const int ManualOverrideSelectionLockInteractions = 8;
    private const int OpeningPeriodTurnCount = 3;

    private void EnsureOpeningToBuildUpTransition(RolePlaySession session)
    {
        if (session.AdaptiveState.CurrentPhase == NarrativePhase.Opening
            && session.AdaptiveState.ObservedTurnCount > OpeningPeriodTurnCount)
        {
            session.AdaptiveState.CurrentPhase = NarrativePhase.BuildUp;
            session.AdaptiveState.InteractionCountInPhase = 0;
            _logger.LogInformation(
                "RolePlayV2 Opening→BuildUp transition: SessionId={SessionId} ObservedTurnCount={ObservedTurns}",
                session.Id,
                session.AdaptiveState.ObservedTurnCount);
        }
    }

    private static readonly string[] GenericLocationNames =
    [
        "Living Room",
        "Game Room",
        "Guest Room",
        "Guest Bedroom",
        "Kitchen",
        "Bedroom",
        "Bathroom",
        "Office",
        "Study",
        "Garden",
        "Patio",
        "Balcony",
        "Hall",
        "Hallway",
        "Corridor",
        "Lounge",
        "Bar",
        "Club",
        "Restaurant",
        "Cafe",
        "Coffee Shop",
        "Outside",
        "Outdoors",
        "Park",
        "Street",
        "Car",
        "Parking Lot",
        "Backyard",
        "Garage",
        "Dining Room",
        "Pool",
        "Library"
    ];

    private static readonly ConcurrentDictionary<string, RolePlaySession> Sessions = new();
    private static readonly IReadOnlyDictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)> DecisionOptionCatalog =
        new Dictionary<string, (string Label, IReadOnlyDictionary<string, int> Deltas)>(StringComparer.OrdinalIgnoreCase)
        {
            ["lean-in"] = ("Lean In", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 6,
                ["Tension"] = 4,
                ["Restraint"] = -20
            }),
            ["tempt-answer"] = ("Tempted Answer", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 8,
                ["Loyalty"] = -6,
                ["Tension"] = 3,
                ["Restraint"] = -18
            }),
            ["hold-back"] = ("Hold Back", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Restraint"] = 5,
                ["Tension"] = -2,
                ["SelfRespect"] = 2
            }),
            ["seek-connection"] = ("Seek Connection", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Connection"] = 5,
                ["Loyalty"] = 3
            }),
            ["test-boundary"] = ("Test Boundary", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 5,
                ["Restraint"] = -20,
                ["Tension"] = 3
            }),
            ["escalate"] = ("Escalate", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Desire"] = 4,
                ["Tension"] = 4,
                ["Restraint"] = -25
            }),
            ["redirect"] = ("Redirect", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Restraint"] = 4,
                ["Connection"] = 2
            }),
            ["observe"] = ("Observe", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tension"] = 1,
                ["Restraint"] = 2
            }),
            ["husband-observes"] = ("Let Him Observe", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tension"] = 2,
                ["Restraint"] = -22
            }),
            ["custom"] = ("Custom Response", new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase))
        };

    private readonly IRolePlayContinuationService _continuationService;
    private readonly IBehaviorModeService _behaviorModeService;
    private readonly IRolePlayPromptRouter _promptRouter;
    private readonly IRolePlayIdentityOptionsService _identityOptionsService;
    private readonly IRolePlayAdaptiveStateService _adaptiveStateService;
    private readonly IRolePlayCommandValidator _commandValidator;
    private readonly ISessionService _sessionService;
    private readonly IScenarioService _scenarioService;
    private readonly ICharacterProfileService _characterProfileService;
    private readonly AutoSaveCoordinator _autoSaveCoordinator;
    private readonly IRolePlayDebugEventSink _debugEventSink;
    private readonly IScenarioSelectionService _scenarioSelectionService;
    private readonly IScenarioLifecycleService _scenarioLifecycleService;
    private readonly IConceptInjectionService _conceptInjectionService;
    private readonly IDecisionPointService _decisionPointService;
    private readonly IRolePlayStateRepository _stateRepository;
    private readonly ICompletionClient? _completionClient;
    private readonly IModelResolutionService? _modelResolutionService;
    private readonly IThemePreferenceService? _themePreferenceService;
    private readonly IRPThemeService? _rpThemeService;
    private readonly IThemeMachineResolutionService? _themeMachineResolutionService;
    private readonly IThemeMachineEvaluator? _themeMachineEvaluator;
    private readonly ITemplateService? _templateService;
    private readonly RolePlayPromptComposer _promptComposer;
    private readonly RolePlaySessionCompatibilityService? _compatibilityService;
    private readonly ILogger<RolePlayEngineService> _logger;
    private readonly decimal _completedScenarioRepeatPenaltyPerRun;
    private readonly decimal _completedScenarioRepeatPenaltyFloor;
    private readonly decimal _completedScenarioRecentPenaltyMultiplier;
    private readonly decimal _completedScenarioThemeScorePenalty;
    private readonly int _completedScenarioThemeCooldownInteractions;
    private readonly decimal _completedScenarioFitScorePenaltyPoints;
    private readonly bool _suppressNarrativeAfterDecision;
    private readonly bool _suppressNarrativeAfterPhaseChange;
    private readonly bool _enablePhaseChangeDecisionPrompts;
    private readonly bool _enableSceneLocationDecisionPrompts;
    private readonly bool _enableLocationServices;
    private readonly bool _enableDecisionPrompts;
    private readonly bool _enableAdaptiveStateUpdates;
    private readonly bool _enableSemanticInference;
    private readonly IClimaxBeatRepository? _climaxBeatRepository;
    private readonly ISemanticBackgroundJobQueue? _backgroundJobQueue;
    private readonly ISemanticInteractionAnalysisRepository? _semanticInteractionAnalysisRepository;
    private readonly IEncounterSummaryService? _encounterSummaryService;
    private readonly IOptions<RolePlayMemoryOptions>? _memoryOptions;
    private readonly IStatWillingnessProfileService? _statWillingnessProfileService;
    private readonly ISemanticEventInferenceService? _semanticEventInferenceService;

    // ---- Encounter participation: keyword heuristic (Change 1: sync tier) --------------------
    private static readonly string[] SexualActivityKeywords = new[]
    {
        // Explicit
        "orgasm", "climax", "cock", "cunt", "pussy", "thrust", "stroke", "cum", "come",
        "fuck", "penetrat", "inside", "slide in", "moan", "groan", "gasp", "shudder",
        "spasm", "pulse", "drip", "wet", "slick", "swollen", "erect", "hard", "stiff",
        "finger", "tongue", "lick", "suck", "taste", "grind", "buck", "ride", "pound",
        "slam", "ejaculat", "semen", "pre-cum", "load", "spurt", "arous", "quiver"
    };

    private static readonly string[] SubtleSexualActivityKeywords = new[]
    {
        // Exhibitionism / voyeurism / subtle erotic
        "blouse", "cleavage", "reveal", "flash", "expose", "brush", "graze", "linger",
        "stare", "watch", "glance", "notice", "press against", "proximity", "tension",
        "charged", "undress", "strip", "bare", "naked", "skin", "curve", "outline",
        "silhouette", "down-blouse", "unbutton", "unzip", "loosen", "slip off",
        "slide down", "show off", "display", "pose", "peek", "glimpse", "ogle",
        "eye", "look", "stare", "gaze", "study", "trace", "roam", "wander",
        "skim", "slide", "pull", "tug", "hitch", "adjust", "shift"
    };

    private static readonly string[] EncounterCompletionKeywords = new[]
    {
        // Orgasm signals
        "orgasm", "climax", "come", "came", "cum", "release", "spent", "afterglow",
        "subside", "fade", "pulse", "spasm",
        // Interruption signals
        "interrupt", "startle", "freeze", "caught", "walk in", "separate", "hide",
        "slip away", "pull out", "withdraw"
    };

    private static bool HasSexualActivityContent(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var lower = text.ToLowerInvariant();
        return SexualActivityKeywords.Any(k => lower.Contains(k))
            || SubtleSexualActivityKeywords.Any(k => lower.Contains(k));
    }

    private static bool ContainsEncounterCompletionKeywords(string? evidenceSpan)
    {
        if (string.IsNullOrWhiteSpace(evidenceSpan)) return false;
        var lower = evidenceSpan.ToLowerInvariant();
        return EncounterCompletionKeywords.Any(k => lower.Contains(k));
    }

    // ---- End encounter participation helpers ------------------------------------------------

    public RolePlayEngineService(
        IRolePlayContinuationService continuationService,
        IBehaviorModeService behaviorModeService,
        IRolePlayPromptRouter promptRouter,
        IRolePlayIdentityOptionsService identityOptionsService,
        IRolePlayAdaptiveStateService adaptiveStateService,
        IRolePlayCommandValidator commandValidator,
        ISessionService sessionService,
        IScenarioService scenarioService,
        ICharacterProfileService characterProfileService,
        AutoSaveCoordinator autoSaveCoordinator,
        IRolePlayDebugEventSink debugEventSink,
        ILogger<RolePlayEngineService> logger,
        IScenarioSelectionService? scenarioSelectionService = null,
        IScenarioLifecycleService? scenarioLifecycleService = null,
        IConceptInjectionService? conceptInjectionService = null,
        IDecisionPointService? decisionPointService = null,
        IRolePlayStateRepository? stateRepository = null,
        ICompletionClient? completionClient = null,
        IModelResolutionService? modelResolutionService = null,
        IThemePreferenceService? themePreferenceService = null,
        IRPThemeService? rpThemeService = null,
        IThemeMachineResolutionService? themeMachineResolutionService = null,
        IThemeMachineEvaluator? themeMachineEvaluator = null,
        RolePlayPromptComposer? promptComposer = null,
        RolePlaySessionCompatibilityService? compatibilityService = null,
        IOptions<StoryAnalysisOptions>? storyAnalysisOptions = null,
        IOptions<RolePlayDecisionOptions>? rolePlayDecisionOptions = null,
        IOptions<RolePlayFeatureFlagsOptions>? rolePlayFeatureFlagsOptions = null,
        ITemplateService? templateService = null,
        IClimaxBeatRepository? climaxBeatRepository = null,
        ISemanticBackgroundJobQueue? backgroundJobQueue = null,
        ISemanticInteractionAnalysisRepository? semanticInteractionAnalysisRepository = null,
        IEncounterSummaryService? encounterSummaryService = null,
        IOptions<RolePlayMemoryOptions>? memoryOptions = null,
        IStatWillingnessProfileService? statWillingnessProfileService = null,
        ISemanticEventInferenceService? semanticEventInferenceService = null)
    {
        _continuationService = continuationService;
        _behaviorModeService = behaviorModeService;
        _promptRouter = promptRouter;
        _identityOptionsService = identityOptionsService;
        _adaptiveStateService = adaptiveStateService;
        _commandValidator = commandValidator;
        _sessionService = sessionService;
        _scenarioService = scenarioService;
        _characterProfileService = characterProfileService;
        _autoSaveCoordinator = autoSaveCoordinator;
        _debugEventSink = debugEventSink;
        _scenarioSelectionService = scenarioSelectionService ?? new NullScenarioSelectionService();
        _scenarioLifecycleService = scenarioLifecycleService ?? new NullScenarioLifecycleService();
        _conceptInjectionService = conceptInjectionService ?? new NullConceptInjectionService();
        _decisionPointService = decisionPointService ?? new NullDecisionPointService();
        _stateRepository = stateRepository ?? new NullRolePlayStateRepository();
        _completionClient = completionClient;
        _modelResolutionService = modelResolutionService;
        _themePreferenceService = themePreferenceService;
        _rpThemeService = rpThemeService;
        _themeMachineResolutionService = themeMachineResolutionService;
        _themeMachineEvaluator = themeMachineEvaluator;
        _promptComposer = promptComposer ?? new RolePlayPromptComposer();
        _compatibilityService = compatibilityService;
        _templateService = templateService;
        _logger = logger;
        _completedScenarioRepeatPenaltyPerRun = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRepeatPenaltyPerRun ?? 0.20, 0d, 1d);
        _completedScenarioRepeatPenaltyFloor = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRepeatPenaltyFloor ?? 0.40, 0d, 1d);
        _completedScenarioRecentPenaltyMultiplier = (decimal)Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioRecentPenaltyMultiplier ?? 0.65, 0d, 1d);
        _completedScenarioThemeScorePenalty = Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioThemeScorePenalty ?? 10, 0, 100);
        _completedScenarioThemeCooldownInteractions = Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioThemeCooldownInteractions ?? 10, 0, 200);
        _completedScenarioFitScorePenaltyPoints = Math.Clamp(storyAnalysisOptions?.Value.CompletedScenarioFitScorePenaltyPoints ?? 20m, 0m, 100m);
        _suppressNarrativeAfterDecision = rolePlayDecisionOptions?.Value.SuppressNarrativeAfterDecision ?? false;
        _suppressNarrativeAfterPhaseChange = rolePlayDecisionOptions?.Value.SuppressNarrativeAfterPhaseChange ?? false;
        _enablePhaseChangeDecisionPrompts = rolePlayDecisionOptions?.Value.EnablePhaseChangeDecisionPrompts ?? false;
        _enableSceneLocationDecisionPrompts = rolePlayDecisionOptions?.Value.EnableSceneLocationDecisionPrompts ?? false;
        _enableLocationServices = rolePlayDecisionOptions?.Value.EnableLocationServices ?? true;
        _enableDecisionPrompts = rolePlayDecisionOptions?.Value.EnableDecisionPrompts ?? false;
        _enableAdaptiveStateUpdates = rolePlayFeatureFlagsOptions?.Value.EnableAdaptiveStateUpdates ?? true;
        _enableSemanticInference = rolePlayFeatureFlagsOptions?.Value.EnableSemanticInference ?? true;
        _climaxBeatRepository = climaxBeatRepository;
        _backgroundJobQueue = backgroundJobQueue;
        _semanticInteractionAnalysisRepository = semanticInteractionAnalysisRepository;
        _encounterSummaryService = encounterSummaryService;
        _memoryOptions = memoryOptions;
        _statWillingnessProfileService = statWillingnessProfileService;
        _semanticEventInferenceService = semanticEventInferenceService;
    }

    public Task<RolePlaySession> CreateSessionAsync(
        string title,
        string? scenarioId = null,
        string personaName = "You",
        string personaDescription = "",
        string? personaTemplateId = null,
        string personaGender = "Unknown",
        string personaRole = "Unknown",
        string? personaRelationTargetId = null,
        CancellationToken cancellationToken = default)
    {
        return CreateSessionAsync(new CreateRolePlaySessionRequest
        {
            Title = title,
            ScenarioId = scenarioId,
            PersonaName = personaName,
            PersonaDescription = personaDescription,
            PersonaTemplateId = personaTemplateId,
            PersonaGender = personaGender,
            PersonaRole = personaRole,
            PersonaRelationTargetId = personaRelationTargetId,
        }, cancellationToken);
    }

    public async Task<RolePlaySession> CreateSessionAsync(
        CreateRolePlaySessionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = new RolePlaySession
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Untitled Role-Play" : request.Title.Trim(),
            ScenarioId = request.ScenarioId,
            PersonaName = string.IsNullOrWhiteSpace(request.PersonaName) ? "You" : request.PersonaName.Trim(),
            PersonaDescription = request.PersonaDescription ?? string.Empty,
            PersonaTemplateId = request.PersonaTemplateId,
            PersonaGender = CharacterGenderCatalog.NormalizeForCharacter(request.PersonaGender),
            PersonaRole = CharacterRoleCatalog.Normalize(request.PersonaRole),
            PersonaRelationTargetId = CharacterRelationCatalog.NormalizeTargetId(request.PersonaRelationTargetId),
            PersonaPerspectiveMode = CharacterPerspectiveMode.FirstPersonInternalMonologue,
            PersonaPhysicalAttributes = request.PersonaPhysicalAttributes,
            MaxMilestonesToInject = request.MaxMilestonesToInject,
        };

        foreach (var kvp in request.CharacterEncounterProfileIds)
        {
            _adaptiveStateService.RebindEncounterProfile(session.AdaptiveState, kvp.Key, kvp.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(request.ScenarioId);
            if (scenario is not null)
            {
                if (string.IsNullOrWhiteSpace(session.PersonaRelationTargetId))
                {
                    var personaRelationSource = scenario.Characters.FirstOrDefault(character =>
                    {
                        var relationTargetId = CharacterRelationCatalog.NormalizeTargetId(character.RelationTargetId);
                        if (CharacterRelationCatalog.IsPersonaTarget(relationTargetId))
                        {
                            return true;
                        }

                        var targetPersonaTemplateId = CharacterRelationCatalog.TryGetPersonaTemplateId(relationTargetId);
                        return !string.IsNullOrWhiteSpace(targetPersonaTemplateId)
                            && string.Equals(targetPersonaTemplateId, session.PersonaTemplateId, StringComparison.OrdinalIgnoreCase);
                    });

                    if (personaRelationSource is not null)
                    {
                        session.PersonaRelationTargetId = personaRelationSource.Id;
                    }
                }

                session.PersonaPerspectiveMode = scenario.DefaultPersonaPerspectiveMode;
                session.SelectedThemeProfileId = scenario.DefaultThemeProfileId;
                session.SelectedIntensityProfileId = scenario.DefaultIntensityProfileId;
                session.AdaptiveIntensityProfileId = scenario.DefaultIntensityProfileId;
                session.SelectedSteeringProfileId = scenario.DefaultSteeringProfileId;
                session.IntensityFloorOverride = scenario.DefaultIntensityFloor;
                session.IntensityCeilingOverride = scenario.DefaultIntensityCeiling;

                // Per-session theme selections override the scenario's default RP theme profile.
                // Set BEFORE SeedFromScenarioAsync so the tracker is seeded from selections exclusively.
                if (request.ThemeSelections.Count > 0)
                {
                    session.SessionThemeSelections = request.ThemeSelections.ToList();
                    // SelectedRPThemeProfileId intentionally left null � SeedFromScenarioAsync uses
                    // the SessionThemeSelections branch when selections are present.
                }
                else
                {
                    session.SelectedRPThemeProfileId = scenario.DefaultRPThemeProfileId;
                }

                var resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(scenario.ResolvedBaseStats);
                if (!string.IsNullOrWhiteSpace(scenario.BaseStatProfileId))
                {
                    var baseStatProfile = await _characterProfileService.GetAsync(scenario.BaseStatProfileId, cancellationToken);
                    if (baseStatProfile is not null)
                    {
                        resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(baseStatProfile.CharacterStats);
                        scenario.ResolvedBaseStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
                    }
                }

                session.CharacterPerspectives = scenario.Characters
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => new RolePlayCharacterPerspective
                    {
                        CharacterId = x.Id,
                        CharacterName = x.Name!.Trim(),
                        PerspectiveMode = x.PerspectiveMode
                    })
                    .ToList();

                foreach (var character in scenario.Characters)
                {
                    if (string.IsNullOrWhiteSpace(character.Name))
                    {
                        continue;
                    }

                    var mergedStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
                    var normalizedCharacterOverrides = AdaptiveStatCatalog.NormalizeComplete(character.BaseStats);
                    foreach (var (statName, statValue) in normalizedCharacterOverrides)
                    {
                        mergedStats[statName] = statValue;
                    }

                    // Apply wizard-provided starting stat overrides for this character.
                    if (request.CharacterStatOverrides.TryGetValue(character.Id, out var wizardStatOverrides))
                    {
                        var normalizedWizardOverrides = AdaptiveStatCatalog.NormalizeComplete(wizardStatOverrides);
                        foreach (var (statName, statValue) in normalizedWizardOverrides)
                        {
                            mergedStats[statName] = statValue;
                        }
                    }

                    if (mergedStats.Count == 0)
                    {
                        continue;
                    }

                    // CharacterId is the runtime lookup key used throughout the engine (character name).
                    // Use the trimmed character name so CharacterStats dict key, CharacterId, and
                    // the CharacterSnapshots list are all consistent after SyncCharacterSnapshots().
                    var charKey = character.Name.Trim();
                    var createdProfile = CharacterStatProfileV2Accessor.CreateDefault(charKey);
                    CharacterStatProfileV2Accessor.SetAllStats(createdProfile, mergedStats);
                    createdProfile.CharacterRole = CharacterRoleCatalog.Normalize(character.Role);
                    session.AdaptiveState.CharacterStats[charKey] = createdProfile;

                    // Seed RuntimeEncounterStats from the selected encounter profile.
                    // CharacterEncounterProfileIds is keyed by character.Id (scenario GUID), not by name,
                    // so we must do this lookup here while character.Id is in scope.
                    if (request.CharacterEncounterProfileIds.TryGetValue(character.Id, out var charEncProfileId)
                        && !string.IsNullOrWhiteSpace(charEncProfileId))
                    {
                        var charDims = BehavioralDimensionCatalog.GetDimensions(createdProfile.CharacterRole);
                        if (charDims.Count > 0)
                        {
                            var charEncProfile = await _characterProfileService.GetAsync(charEncProfileId, cancellationToken);
                            if (charEncProfile?.EncounterStats is { Count: > 0 })
                            {
                                createdProfile.RuntimeEncounterStats = charDims.ToDictionary(
                                    d => d.Name,
                                    d => charEncProfile.EncounterStats.TryGetValue(d.Name, out var v) ? v : 50,
                                    StringComparer.OrdinalIgnoreCase);
                            }
                        }
                    }
                }

                await _adaptiveStateService.SeedFromScenarioAsync(session, scenario, cancellationToken);
            }
        }

        await SeedPersonaStatsFromTemplateAsync(session, cancellationToken);

        // Ensure the persona block exists before applying overrides.
        // EnsurePersonaCharacterState is a no-op when SeedPersonaStatsFromTemplateAsync
        // already created it; when no template is set this creates the placeholder block
        // so the stat-override application below can find it by name.
        EnsurePersonaCharacterState(session);

        // Apply wizard-provided persona stat overrides after template seeding.
        // The overrides represent the user's explicit profile choice, so update BOTH the
        // current stats and the baseline � the baseline identifies which stat profile the
        // session "started from" in the Adaptive panel display.
        if (request.PersonaStatOverrides.Count > 0
            && session.AdaptiveState.CharacterStats.TryGetValue(session.PersonaName, out var personaBlock))
        {
            var normalizedPersonaOverrides = AdaptiveStatCatalog.NormalizeComplete(request.PersonaStatOverrides);
            foreach (var (statName, statValue) in normalizedPersonaOverrides)
            {
                CharacterStatProfileV2Accessor.SetStat(personaBlock, statName, statValue);
                personaBlock.BaselineStats[statName] = statValue;
            }
        }

        // Stamp CharacterRole on the persona block (and any character missing it).
        var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
        if (!string.IsNullOrWhiteSpace(personaRole)
            && session.AdaptiveState.CharacterStats.TryGetValue(session.PersonaName, out var personaBlockForRole)
            && string.IsNullOrWhiteSpace(personaBlockForRole.CharacterRole))
        {
            personaBlockForRole.CharacterRole = personaRole;
        }

        // Seed RuntimeEncounterStats for persona from the selected encounter profile.
        // The wizard stores the persona's encounter profile under the "__persona__" key.
        if (request.CharacterEncounterProfileIds.TryGetValue("__persona__", out var personaEncProfileId)
            && !string.IsNullOrWhiteSpace(personaEncProfileId)
            && session.AdaptiveState.CharacterStats.TryGetValue(session.PersonaName, out var personaEncBlock)
            && personaEncBlock.RuntimeEncounterStats is not { Count: > 0 })
        {
            var personaDims = BehavioralDimensionCatalog.GetDimensions(personaEncBlock.CharacterRole ?? string.Empty);
            if (personaDims.Count > 0)
            {
                var personaEncProfile = await _characterProfileService.GetAsync(personaEncProfileId, cancellationToken);
                if (personaEncProfile?.EncounterStats is { Count: > 0 })
                {
                    personaEncBlock.RuntimeEncounterStats = personaDims.ToDictionary(
                        d => d.Name,
                        d => personaEncProfile.EncounterStats.TryGetValue(d.Name, out var v) ? v : 50,
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        // Seal session-start baseline for all character stat blocks.
        // This snapshot is the reference for the "Base" column in the Adaptive panel
        // and the per-character decay target for the Reset phase algorithm.
        foreach (var block in session.AdaptiveState.CharacterStats.Values)
        {
            if (block.BaselineStats.Count == 0)
            {
                block.BaselineStats = CharacterStatProfileV2Accessor.GetAllStats(block);
            }
        }

        // Seed RuntimeEncounterStats at neutral baseline (50) for any character that still
        // has no encounter stats at this point (no encounter profile was selected in the wizard).
        // Characters with a selected profile were already seeded in the character loop above.
        foreach (var block in session.AdaptiveState.CharacterStats.Values)
        {
            if (string.IsNullOrWhiteSpace(block.CharacterRole)
                || block.RuntimeEncounterStats is { Count: > 0 })
                continue;

            var dims = BehavioralDimensionCatalog.GetDimensions(block.CharacterRole);
            if (dims.Count > 0)
                block.RuntimeEncounterStats = dims.ToDictionary(d => d.Name, _ => 50, StringComparer.OrdinalIgnoreCase);
        }

        // Assign the default willingness profile for new sessions.
        if (string.IsNullOrWhiteSpace(session.AdaptiveState.SelectedWillingnessProfileId) && _statWillingnessProfileService is not null)
        {
            var defaultWillingness = await _statWillingnessProfileService.GetDefaultAsync(cancellationToken);
            if (defaultWillingness is not null)
            {
                session.AdaptiveState.SelectedWillingnessProfileId = defaultWillingness.Id;
            }
        }

        // Propagate the session ID into AdaptiveState (required by SaveAdaptiveStateAsync) and
        // sync the runtime CharacterStats dictionary into CharacterSnapshots so the persisted
        // PayloadJson and the V2 state row both contain the seeded character data.
        session.AdaptiveState.SessionId = session.Id;
        session.AdaptiveState.SyncCharacterSnapshots();

        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-created");

        // Eagerly write the initial V2 state row so the workspace Adaptive panel can display
        // characters immediately without waiting for the first turn to complete.
        await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            EventKind = "SessionCreated",
            Severity = "Info",
            ActorName = session.PersonaName,
            Summary = "Role-play session created",
            MetadataJson = JsonSerializer.Serialize(new
            {
                session.Id,
                session.Title,
                session.PersonaName,
                session.ScenarioId
            })
        }, cancellationToken);

        _logger.LogInformation("Role-play session created: {SessionId} ({Title}), Persona={PersonaName}",
            session.Id, session.Title, session.PersonaName);
        return session;
    }

    public async Task<IReadOnlyList<RolePlaySession>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        await EnsurePersistedSessionsLoadedAsync(cancellationToken);

        IReadOnlyList<RolePlaySession> results = Sessions.Values
            .OrderByDescending(x => x.ModifiedAt)
            .ToList();

        _logger.LogInformation("Retrieved {Count} role-play sessions", results.Count);
        return results;
    }

    public void InvalidateSessionCache(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        Sessions.TryRemove(sessionId, out _);
    }

    public async Task<RolePlaySession?> GetSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (Sessions.TryGetValue(sessionId, out var session))
        {
            if (EnsurePersonaCharacterState(session))
            {
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-persona-character-normalized");
            }

            return session;
        }

        session = await _sessionService.LoadRolePlaySessionAsync(sessionId, cancellationToken);
        if (session is not null)
        {
            if (EnsurePersonaCharacterState(session))
            {
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-persona-character-normalized");
            }

            Sessions[session.Id] = session;
        }

        return session;
    }

    public async Task<RolePlaySession> OpenSessionAsync(
        string sessionId,
        RolePlaySessionOpenAction action,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        switch (action)
        {
            case RolePlaySessionOpenAction.Start when session.Status != RolePlaySessionStatus.NotStarted:
                throw new InvalidOperationException($"Session '{sessionId}' cannot be started because it is {session.Status}.");

            case RolePlaySessionOpenAction.Continue when session.Status != RolePlaySessionStatus.InProgress:
                throw new InvalidOperationException($"Session '{sessionId}' cannot be continued because it is {session.Status}.");

            case RolePlaySessionOpenAction.Start:
                session.Status = RolePlaySessionStatus.InProgress;
                session.ModifiedAt = DateTime.UtcNow;
                _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-started");
                break;
        }

        _logger.LogInformation(
            SessionLogEvents.OpenRolePlaySession,
            "Role-play session opened: {SessionId}, action={Action}, status={Status}",
            session.Id,
            action,
            session.Status);

        return session;
    }

    public Task<RolePlaySession> SaveSessionAsync(RolePlaySession session, CancellationToken cancellationToken = default)
    {
        session.ModifiedAt = DateTime.UtcNow;
        Sessions[session.Id] = session;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-session-updated");
        _logger.LogInformation("Role-play session saved: {SessionId}, interactions={Count}, mode={Mode}", session.Id, session.Interactions.Count, session.BehaviorMode);
        return Task.FromResult(session);
    }

    public async Task<RolePlaySession> RebuildAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        await RebuildAdaptiveStateInternalAsync(session, cancellationToken);
        await SaveSessionAsync(session, cancellationToken);
        return session;
    }

    public async Task<RolePlaySession> OverrideAdaptiveThemeAsync(
        string sessionId,
        string requestedThemeId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");

        if (string.IsNullOrWhiteSpace(requestedThemeId))
        {
            throw new ArgumentException("Requested theme id is required.", nameof(requestedThemeId));
        }

        var applied = await _adaptiveStateService.ApplyManualScenarioOverrideAsync(session, requestedThemeId, cancellationToken);
        if (!applied)
        {
            throw new InvalidOperationException($"Theme '{requestedThemeId}' is not available for manual override.");
        }

        session.ModifiedAt = DateTime.UtcNow;
        await SaveSessionAsync(session, cancellationToken);

        _logger.LogInformation(
            "Manual adaptive theme override applied for session {SessionId}: requestedThemeId={ThemeId}",
            sessionId,
            requestedThemeId);

        return session;
    }

    public async Task<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var removedFromCache = Sessions.TryRemove(sessionId, out _);
        var deletedPersisted = await _sessionService.DeleteAsync(sessionId, cancellationToken);
        var deleted = removedFromCache || deletedPersisted;

        if (deleted)
        {
            _logger.LogInformation(SessionLogEvents.DeleteRolePlaySession, "Role-play session hard-deleted: {SessionId}", sessionId);
        }
        else
        {
            _logger.LogWarning("Role-play session delete requested for missing session: {SessionId}", sessionId);
        }

        return deleted;
    }

    public async Task<bool> UpdateBehaviorModeAsync(string sessionId, BehaviorMode mode, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        _behaviorModeService.SetMode(session, mode);
        await SaveSessionAsync(session, cancellationToken);
        return true;
    }

    public async Task<RolePlayInteraction> AddInteractionAsync(
        string sessionId,
        ContinueAsActor actor,
        string content,
        string? customActorName = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Content is required.", nameof(content));
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var interaction = new RolePlayInteraction
        {
            InteractionType = ToInteractionType(actor),
            ActorName = ResolveActorName(actor, customActorName),
            Content = content.Trim()
        };

        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "AddInteraction",
            actor.ToString(),
            interaction.ActorName,
            null,
            cancellationToken);
        session.AdaptiveState.ObservedTurnCount++;
        EnsureOpeningToBuildUpTransition(session);

        var outputInteractionIds = new List<string>();
        var turnSucceeded = false;
        string? turnFailureReason = null;
        try
        {
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.Status = RolePlaySessionStatus.InProgress;
            session.ModifiedAt = DateTime.UtcNow;

            await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
            await RunRolePlayV2PipelinesAsync(
                session,
                DecisionTrigger.InteractionStart,
                cancellationToken);

            // Reset turn tracking if user acted manually
            if (actor == ContinueAsActor.You)
            {
                session.ConsecutiveNpcTurns = 0;
                session.CurrentTurnState = TurnState.NpcTurn;
            }

            _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-interaction-added");
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "InteractionPersisted",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Manual interaction added",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interaction.Id,
                    interaction.ActorName,
                    interaction.InteractionType,
                    interaction.Content
                })
            }, cancellationToken);

            _logger.LogInformation("Manual role-play interaction appended to session {SessionId} as {Actor}", sessionId, interaction.ActorName);
            turnSucceeded = true;
            return interaction;
        }
        catch (Exception ex)
        {
            turnFailureReason = ex.Message;
            throw;
        }
        finally
        {
            if (turnSucceeded && session.AdaptiveState.IsStateDirty)
            {
                await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
                session.AdaptiveState.IsStateDirty = false;
            }
            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                turnSucceeded,
                turnFailureReason,
                cancellationToken);
        }
    }

    public async Task<RolePlayInteraction> ContinueAsync(
        string sessionId,
        ContinueAsActor actor,
        string? customActorName = null,
        string? instruction = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{sessionId}' not found.");
        }

        if (!_behaviorModeService.IsContinuationAllowed(session.BehaviorMode, actor, explicitSelection: true))
        {
            throw new InvalidOperationException($"Actor '{actor}' is not allowed in mode '{session.BehaviorMode}'.");
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);
        await SeedPersonaStatsFromTemplateAsync(session, cancellationToken);

        var promptText = string.IsNullOrWhiteSpace(instruction)
            ? "Continue the scene naturally."
            : instruction.Trim();
        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "Continue",
            actor.ToString(),
            ResolveActorName(actor, customActorName),
            null,
            cancellationToken);
        session.AdaptiveState.ObservedTurnCount++;
        EnsureOpeningToBuildUpTransition(session);

        var outputInteractionIds = new List<string>();
        var turnSucceeded = false;
        string? turnFailureReason = null;
        try
        {
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);

            var interaction = await _continuationService.ContinueNarrativeAsync(
                session,
                ResolveActorName(actor, customActorName),
                promptText,
                cancellationToken);

            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            session.Status = RolePlaySessionStatus.InProgress;
            session.ModifiedAt = DateTime.UtcNow;
            await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
            await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);
            // Flush the pending session save to DB before enqueueing the semantic analysis job.
            // The job handler loads the session from DB; if we only queue the save (debounced 1s),
            // the background runner will read a stale snapshot that does not contain the new interaction.
            _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continue-generated");
            await _autoSaveCoordinator.FlushAsync(cancellationToken);
            await QueueSemanticInteractionAnalysisAsync(session, interaction, cancellationToken);
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "InteractionPersisted",
                Severity = "Info",
                ActorName = interaction.ActorName,
                ModelIdentifier = interaction.GeneratedByModelId,
                ProviderName = interaction.GeneratedByProvider,
                Summary = "Continuation interaction persisted",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interaction.Id,
                    interaction.ActorName,
                    interaction.InteractionType,
                    interaction.GeneratedByCommand,
                    interaction.GeneratedByModelId,
                    interaction.GeneratedByProvider
                })
            }, cancellationToken);

            _logger.LogInformation(
                "Role-play continuation generated for session {SessionId} as {Actor} in mode {Mode}",
                sessionId,
                interaction.ActorName,
                session.BehaviorMode);

            turnSucceeded = true;
            return interaction;
        }
        catch (Exception ex)
        {
            turnFailureReason = ex.Message;
            throw;
        }
        finally
        {
            if (turnSucceeded && session.AdaptiveState.IsStateDirty)
            {
                await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
                session.AdaptiveState.IsStateDirty = false;
            }
            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                turnSucceeded,
                turnFailureReason,
                cancellationToken);
        }
    }

    public async Task<RolePlayInteraction> SubmitPromptAsync(
        UnifiedPromptSubmission submission,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        if (!_commandValidator.ValidateSubmission(submission, out var validationError))
        {
            throw new ArgumentException(validationError, nameof(submission));
        }

        var session = await GetSessionAsync(submission.SessionId, cancellationToken);
        if (session is null)
        {
            throw new InvalidOperationException($"Role-play session '{submission.SessionId}' not found.");
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        IdentityOption identity;
        string? customName;
        if (submission.Intent == PromptIntent.Instruction)
        {
            identity = new IdentityOption
            {
                Id = "system:instruction",
                DisplayName = "Instruction",
                SourceType = IdentityOptionSource.Persona,
                Actor = ContinueAsActor.Npc,
                IsAvailable = true
            };
            customName = null;
        }
        else
        {
            var options = await _identityOptionsService.GetIdentityOptionsAsync(session, cancellationToken);
            identity = options.FirstOrDefault(x =>
                x.SourceType == submission.SelectedIdentityType &&
                string.Equals(x.Id, submission.SelectedIdentityId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Identity option '{submission.SelectedIdentityId}' is not available for this session.");

            if (submission.SubmittedVia != SubmissionSource.PlusButton && !identity.IsAvailable)
            {
                throw new InvalidOperationException(identity.AvailabilityReason ?? "The selected identity is not available.");
            }

            customName = identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(submission.CustomIdentityName) ? null : submission.CustomIdentityName.Trim())
                : null;
        }

        var initiatedByActorName = submission.Intent == PromptIntent.Instruction
            ? "Instruction"
            : (identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(customName) ? identity.DisplayName : customName)
                : identity.DisplayName);

        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "SubmitPrompt",
            submission.SubmittedVia.ToString(),
            initiatedByActorName,
            null,
            cancellationToken);
        session.AdaptiveState.ObservedTurnCount++;
        EnsureOpeningToBuildUpTransition(session);
        var outputInteractionIds = new List<string>();

        var route = _promptRouter.Resolve(submission.Intent);
        _logger.LogInformation(
            "Unified prompt route selected for session {SessionId}: intent={Intent}, command={Command}, identity={IdentityId}",
            submission.SessionId,
            submission.Intent,
            route.TargetCommand,
            identity.Id);

        RolePlayInteraction interaction;
        if (submission.Intent == PromptIntent.Instruction)
        {
            interaction = new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = "Instruction",
                Content = submission.PromptText.Trim()
            };
        }
        else
        {
            if (submission.SubmittedVia != SubmissionSource.PlusButton
                && !_identityOptionsService.IsIdentityAvailableForIntent(session, identity, submission.Intent, out var availabilityReason))
            {
                throw new InvalidOperationException(availabilityReason ?? "The selected identity is not available for this action.");
            }

            var selectedActorName = identity.SourceType == IdentityOptionSource.CustomCharacter
                ? (string.IsNullOrWhiteSpace(customName) ? identity.DisplayName : customName)
                : identity.DisplayName;

            var userPromptInteraction = new RolePlayInteraction
            {
                InteractionType = ToInteractionType(identity.Actor),
                ActorName = selectedActorName,
                Content = submission.PromptText.Trim()
            };

            session.Interactions.Add(userPromptInteraction);
            outputInteractionIds.Add(userPromptInteraction.Id);
            await UpdateStateAndDetectEncounterAsync(session, userPromptInteraction, cancellationToken);

            if (submission.SubmittedVia == SubmissionSource.PlusButton)
            {
                interaction = userPromptInteraction;
            }
            else
            {
                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);

                interaction = await _continuationService.ContinueAsync(
                    session,
                    identity.Actor,
                    selectedActorName,
                    submission.Intent,
                    BuildContinuationPromptText(submission.Intent, submission.PromptText),
                    onChunk,
                    cancellationToken);

                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
            }
        }

        if (submission.Intent == PromptIntent.Instruction)
        {
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
        }

        session.Status = RolePlaySessionStatus.InProgress;
        session.BehaviorMode = submission.BehaviorModeAtSubmit;
        session.ModifiedAt = DateTime.UtcNow;

        // Reset turn tracking when user submits any message � it's no longer "their turn"
        if (submission.Intent != PromptIntent.Instruction)
        {
            session.ConsecutiveNpcTurns = 0;
            session.CurrentTurnState = TurnState.NpcTurn;
        }

        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-unified-prompt-submitted");
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
        {
            SessionId = session.Id,
            InteractionId = interaction.Id,
            EventKind = "PromptSubmitted",
            Severity = "Info",
            ActorName = interaction.ActorName,
            ModelIdentifier = interaction.GeneratedByModelId,
            ProviderName = interaction.GeneratedByProvider,
            Summary = "Unified prompt submission completed",
            MetadataJson = JsonSerializer.Serialize(new
            {
                submission.Intent,
                submission.SubmittedVia,
                submission.SelectedIdentityId,
                submission.SelectedIdentityType,
                interaction.Id,
                interaction.ActorName
            })
        }, cancellationToken);

        _logger.LogInformation(
            "Unified prompt executed for session {SessionId}: actor={Actor}, mode={Mode}",
            session.Id,
            interaction.ActorName,
            session.BehaviorMode);

        var steerDirective = string.Empty;
        var steerCommandRequested = submission.Intent == PromptIntent.Instruction
            && TryExtractSteerDirective(submission.PromptText, out steerDirective);
        if (steerCommandRequested)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "SteerCommandApplied",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Steer command applied without phase progression",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    command = "/steer",
                    directive = steerDirective,
                    currentPhase = session.AdaptiveState.CurrentPhase.ToString(),
                    activeThemeId = session.AdaptiveState.ActiveScenarioId,
                    currentSceneLocation = session.AdaptiveState.CurrentSceneLocation
                })
            }, cancellationToken);

            if (session.AdaptiveState.IsStateDirty)
            {
                await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
                session.AdaptiveState.IsStateDirty = false;
            }
            await _stateRepository.CompleteTurnAsync(
                session.Id,
                persistedTurn.TurnId,
                outputInteractionIds,
                succeeded: true,
                cancellationToken: cancellationToken);

            return interaction;
        }

        var nextPhaseCommandRequested = submission.Intent == PromptIntent.Instruction
            && ContainsNextPhaseCommand(submission.PromptText);
        var explicitClimaxCompletionRequested = ContainsClimaxCompletionCommand(submission.PromptText);
        // Always align V2 state into AdaptiveState immediately before resolving the manual phase target.
        // This prevents V1 pipeline mutations (from UpdateFromInteractionAsync above) from polluting
        // the phase used by ResolveManualPhaseAdvanceTarget, which must reflect the V2 canonical state.
        await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
        var phaseBeforePipeline = session.AdaptiveState.CurrentPhase;
        var activeScenarioBeforePipeline = session.AdaptiveState.ActiveScenarioId;
        var manualPhaseAdvanceTarget = ResolveManualPhaseAdvanceTarget(submission.PromptText, phaseBeforePipeline);

        if (nextPhaseCommandRequested || explicitClimaxCompletionRequested)
        {
            _logger.LogInformation(
                "Phase command received: SessionId={SessionId} CommandText={CommandText} CurrentPhase={CurrentPhase} ManualTarget={ManualTarget} ClimaxCompletion={ClimaxCompletion}",
                session.Id,
                submission.PromptText,
                phaseBeforePipeline,
                manualPhaseAdvanceTarget?.ToString() ?? "(none)",
                explicitClimaxCompletionRequested);
        }

        // /completeclimax: generate the finish-move narrative under Climax phase FIRST,
        // then transition Climax -> Reset. Only triggered when phase is still Climax �
        // if the session is not in Climax the finish-move narrative is skipped
        // and the standard pipeline path handles the interaction instead.
        bool pipelinesAlreadyRan = false;
        if (explicitClimaxCompletionRequested
            && phaseBeforePipeline == NarrativePhase.Climax)
        {
            // Step 1: generate multi-actor finish-move responses in Climax phase context.
            // Each scene character responds to the directive, then narrative closes the turn.
            TryExtractClimaxCompletionDirective(submission.PromptText, out var completionDirective);
            if (string.IsNullOrWhiteSpace(completionDirective))
            {
                completionDirective = "Write the final beat of the climax � close the moment decisively and resolve the immediate tension. End the scene cleanly.";
            }

            var sceneActors = await ResolveSceneContinueActorsAsync(session, cancellationToken);
            var batchSize = Math.Max(1, Math.Min(session.SceneContinueBatchSize, sceneActors.Count));
            var finalClimaxInteraction = default(RolePlayInteraction);

            for (var i = 0; i < batchSize; i++)
            {
                var candidate = sceneActors[i];
                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
                var actorInteraction = await _continuationService.ContinueAsync(
                    session, candidate.Actor, candidate.Name, PromptIntent.Message,
                    completionDirective, onChunk, cancellationToken);

                session.Interactions.Add(actorInteraction);
                outputInteractionIds.Add(actorInteraction.Id);
                await UpdateStateAndDetectEncounterAsync(session, actorInteraction, cancellationToken);
                finalClimaxInteraction = actorInteraction;
            }

            // Narrative close under Climax writing rules.
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var narrativeInteraction = await _continuationService.ContinueNarrativeAsync(
                session, "Narrative", completionDirective, cancellationToken);
            session.Interactions.Add(narrativeInteraction);
            outputInteractionIds.Add(narrativeInteraction.Id);
            await UpdateStateAndDetectEncounterAsync(session, narrativeInteraction, cancellationToken);
            finalClimaxInteraction = narrativeInteraction;

            // Step 2: advance phase to Reset AFTER the climax completion is written.
            await RunRolePlayV2PipelinesAsync(
                session,
                DecisionTrigger.InteractionStart,
                cancellationToken,
                explicitClimaxCompletionRequested: true,
                manualPhaseAdvanceTarget);
            pipelinesAlreadyRan = true;

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = finalClimaxInteraction.Id,
                EventKind = "ClimaxCompletionFinalWriteGenerated",
                Severity = "Info",
                ActorName = finalClimaxInteraction.ActorName,
                ModelIdentifier = finalClimaxInteraction.GeneratedByModelId,
                ProviderName = finalClimaxInteraction.GeneratedByProvider,
                Summary = $"Finish-move generated with {batchSize} scene actor(s) + narrative in Climax phase before Climax -> Reset transition.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    directive = completionDirective,
                    phaseAtGeneration = "Climax",
                    actorCount = batchSize,
                    interactionId = finalClimaxInteraction.Id,
                    activeScenarioId = activeScenarioBeforePipeline
                })
            }, cancellationToken);

            interaction = finalClimaxInteraction;
        }

        if (!pipelinesAlreadyRan)
        {
            await RunRolePlayV2PipelinesAsync(
                session,
                DecisionTrigger.InteractionStart,
                cancellationToken,
                explicitClimaxCompletionRequested,
                manualPhaseAdvanceTarget);
        }

        if (nextPhaseCommandRequested || explicitClimaxCompletionRequested)
        {
            var phaseAfterPipeline = session.AdaptiveState.CurrentPhase;
            var activeScenarioAfterPipeline = session.AdaptiveState.ActiveScenarioId;
            var phaseChanged = phaseAfterPipeline != phaseBeforePipeline;

            if (!phaseChanged)
            {
                _logger.LogWarning(
                    "Phase command completed without phase change: SessionId={SessionId} CommandText={CommandText} Phase={Phase} ManualTarget={ManualTarget} ClimaxCompletion={ClimaxCompletion} ActiveScenarioBefore={ActiveScenarioBefore} ActiveScenarioAfter={ActiveScenarioAfter}",
                    session.Id,
                    submission.PromptText,
                    phaseAfterPipeline,
                    manualPhaseAdvanceTarget?.ToString() ?? "(none)",
                    explicitClimaxCompletionRequested,
                    activeScenarioBeforePipeline ?? string.Empty,
                    activeScenarioAfterPipeline ?? string.Empty);
            }
            else
            {
                _logger.LogInformation(
                    "Phase command advanced phase: SessionId={SessionId} CommandText={CommandText} FromPhase={FromPhase} ToPhase={ToPhase} ActiveScenario={ActiveScenario}",
                    session.Id,
                    submission.PromptText,
                    phaseBeforePipeline,
                    phaseAfterPipeline,
                    activeScenarioAfterPipeline ?? string.Empty);
            }
        }

        if (session.AdaptiveState.IsStateDirty)
        {
            await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
            session.AdaptiveState.IsStateDirty = false;
        }
        await _stateRepository.CompleteTurnAsync(
            session.Id,
            persistedTurn.TurnId,
            outputInteractionIds,
            succeeded: true,
            cancellationToken: cancellationToken);

        return interaction;
    }

    public async Task<ContinueAsResult> ContinueAsAsync(
        ContinueAsRequest request,
        Func<string, Task>? onChunk = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await GetSessionAsync(request.SessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Role-play session '{request.SessionId}' not found.");

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);
        await SeedPersonaStatsFromTemplateAsync(session, cancellationToken);

        if (!_commandValidator.ValidateContinueRequest(request, session.BehaviorMode, out var validationError))
        {
            return new ContinueAsResult
            {
                Success = false,
                ValidationError = validationError,
                IsClearResult = request.IsClearAction
            };
        }

        if (request.IsClearAction)
        {
            _logger.LogInformation("Continue As selections cleared for session {SessionId}", request.SessionId);
            return new ContinueAsResult { Success = true, IsClearResult = true };
        }

        // If it was the user's turn but they clicked continue, reset turn tracking and proceed
        if (session.BehaviorMode == BehaviorMode.TakeTurns
            && session.CurrentTurnState == TurnState.UserTurn
            && request.TriggeredBy == SubmissionSource.MainOverflowContinue)
        {
            session.ConsecutiveNpcTurns = 0;
            session.CurrentTurnState = TurnState.NpcTurn;
        }

        var selectedIdentityOptions = await ResolveSelectedIdentityOptionsAsync(session, request, cancellationToken);
        var result = new ContinueAsResult { Success = true };
        var persistedTurn = await _stateRepository.StartTurnAsync(
            session.Id,
            "ContinueAs",
            request.TriggeredBy.ToString(),
            string.IsNullOrWhiteSpace(request.CustomIdentityName) ? session.PersonaName : request.CustomIdentityName,
            null,
            cancellationToken);
        session.AdaptiveState.ObservedTurnCount++;
        EnsureOpeningToBuildUpTransition(session);
        var outputInteractionIds = new List<string>();

        var isOverflowContinue = request.TriggeredBy == SubmissionSource.MainOverflowContinue;
        int? turnActorCount = null;  // set in overflow path, used by narrative call

        // --- OPENING NARRATIVE ---
        // If no interactions yet, always generate a scene-setting narrative FIRST
        var isOpeningScene = session.Interactions.Count(i => !i.IsExcluded) == 0;
        if (isOpeningScene && session.AutoNarrative)
        {
            var openingPrompt = await BuildOpeningNarrativePromptAsync(session, cancellationToken);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var openingNarrative = await _continuationService.ContinueNarrativeAsync(
                session,
                "Narrative",
                openingPrompt,
                cancellationToken);
            result.NarrativeOutput = openingNarrative;
            session.Interactions.Add(openingNarrative);
            outputInteractionIds.Add(openingNarrative.Id);
            await UpdateStateAndDetectEncounterAsync(session, openingNarrative, cancellationToken);
        }

        if (selectedIdentityOptions.Count > 0)
        {
            // Explicit identity selections � generate sequentially, accumulating context
            foreach (var option in selectedIdentityOptions)
            {
                var actorName = ResolveOptionActorName(option, request.CustomIdentityName);
                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
                var interaction = await _continuationService.ContinueAsync(
                    session,
                    option.Actor,
                    actorName,
                    PromptIntent.Message,
                    "Continue role-play for the selected character.",
                    onChunk,
                    cancellationToken);

                result.ParticipantOutputs.Add(interaction);
                // Add to session immediately so the next generation sees this in its context
                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
            }
        }
        else if (isOverflowContinue)
        {
            // --- MULTI-ACTOR OVERFLOW CONTINUE ---
            // Determine which scene characters should naturally respond,
            // generate sequentially so each sees the prior output in context.
            var sceneActors = await ResolveSceneContinueActorsAsync(session, cancellationToken);

            var batchSize = Math.Max(1, Math.Min(session.SceneContinueBatchSize, sceneActors.Count));
            turnActorCount = batchSize;
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "OverflowActorSelection",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                Summary = $"Overflow actor auto-selection resolved ({batchSize} of {sceneActors.Count} candidates).",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = request.TriggeredBy.ToString(),
                    mode = session.BehaviorMode.ToString(),
                    batchSize,
                    candidates = sceneActors.Select((x, index) => new
                    {
                        rank = index + 1,
                        actor = x.Actor.ToString(),
                        name = x.Name,
                        reason = x.Reason,
                        selected = index < batchSize
                    }).ToList()
                })
            }, cancellationToken);

            // Multi-encounter Climax: when a time-skip phase is set (boundary detection fired),
            // reset the encounter-interaction counter but do NOT null the phase.
            // The phase-branched injection block below handles each stage.
            var isClimaxPhase = string.Equals(session.AdaptiveState.CurrentPhase.ToString(), "Climax", StringComparison.OrdinalIgnoreCase);
            if (isClimaxPhase && session.AdaptiveState.CurrentTimeSkipPhase != TimeSkipPhase.None)
            {
                session.AdaptiveState.InteractionsInCurrentEncounter = 0;
                // Phase intentionally preserved — the injection block below branches on it.
            }

            // Multi-encounter Climax: two-turn time-skip split (FR-001).
            //   CloseScene → inject close directive, advance phase to AdvanceTime.
            //   AdvanceTime → inject advance directive, advance phase to None.
            // FR-005: defer when a recent user-authored instruction exists.
            RolePlayInteraction? injectedTimeSkipInstruction = null;
            if (isClimaxPhase
                && session.AdaptiveState.CurrentTimeSkipPhase != TimeSkipPhase.None
                && session.AdaptiveState.CurrentEncounterNumber > 0
                && _rpThemeService is not null
                && !string.IsNullOrWhiteSpace(session.AdaptiveState.ActiveScenarioId))
            {
                RPTheme? theme = null;
                try { theme = await _rpThemeService.GetThemeAsync(session.AdaptiveState.ActiveScenarioId, cancellationToken); }
                catch (Exception ex) { _logger.LogDebug(ex, "MultiEncounter time-skip: could not load theme"); }

                if (theme is not null && RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax"))
                {
                    // FR-005: defer when the user recently authored an instruction
                    if (!HasRecentUserInstruction(session, 3))
                    {
                        var currentSkipPhase = session.AdaptiveState.CurrentTimeSkipPhase;
                        string directive;
                        if (currentSkipPhase == TimeSkipPhase.CloseScene)
                        {
                            directive = "Close the current encounter naturally.";
                            session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.AdvanceTime;
                            session.AdaptiveState.IsStateDirty = true;
                        }
                        else // AdvanceTime
                        {
                            directive = "Advance time to a new moment — a different day or time, a new context, a new circumstance. Establish ordinary life.";
                            session.AdaptiveState.CurrentTimeSkipPhase = TimeSkipPhase.None;
                            session.AdaptiveState.IsStateDirty = true;
                        }

                        var timeSkipInstruction = new RolePlayInteraction
                        {
                            InteractionType = InteractionType.System,
                            ActorName = "Instruction",
                            Content = directive,
                            NarrativePhaseAtCreation = session.AdaptiveState.CurrentPhase,
                            GeneratedByCommand = "MultiEncounterTimeSkip"
                        };
                        injectedTimeSkipInstruction = timeSkipInstruction;
                        session.Interactions.Add(timeSkipInstruction);
                        outputInteractionIds.Add(timeSkipInstruction.Id);
                        await UpdateStateAndDetectEncounterAsync(session, timeSkipInstruction, cancellationToken);

                        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                        {
                            SessionId = session.Id,
                            InteractionId = timeSkipInstruction.Id,
                            EventKind = "MultiEncounterInstructionInjected",
                            Severity = "Info",
                            ActorName = "Instruction",
                            Summary = $"Multi-encounter time-skip {currentSkipPhase} directive injected for encounter #{session.AdaptiveState.CurrentEncounterNumber}.",
                            MetadataJson = JsonSerializer.Serialize(new
                            {
                                encounterNumber = session.AdaptiveState.CurrentEncounterNumber,
                                phase = currentSkipPhase.ToString(),
                                directive
                            })
                        }, cancellationToken);
                    }
                    else
                    {
                        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                        {
                            SessionId = session.Id,
                            EventKind = "MultiEncounterTimeSkipDeferred",
                            Severity = "Info",
                            Summary = $"Multi-encounter time-skip deferred (recent user instruction). Phase={session.AdaptiveState.CurrentTimeSkipPhase}",
                            MetadataJson = JsonSerializer.Serialize(new
                            {
                                encounterNumber = session.AdaptiveState.CurrentEncounterNumber,
                                phase = session.AdaptiveState.CurrentTimeSkipPhase.ToString()
                            })
                        }, cancellationToken);
                    }
                }
            }

            for (var i = 0; i < batchSize; i++)
            {
                var candidate = sceneActors[i];
                var actor = candidate.Actor;
                var actorName = candidate.Name;
                var positionInTurn = i + 1; // 1-based

                // Multi-encounter Climax: adjust the per-position prompt to reflect encounter state.
                string promptText;
                if (isClimaxPhase)
                {
                    if (i == 0)
                    {
                        // FR-011: only treat as new-encounter-start when no time-skip phase is active.
                        var isNewEncounterStart = session.AdaptiveState.CurrentTimeSkipPhase == TimeSkipPhase.None
                            && session.AdaptiveState.CurrentEncounterNumber > 0
                            && session.AdaptiveState.InteractionsInCurrentEncounter == 0;
                        promptText = isNewEncounterStart
                            ? "Begin a new encounter — a discrete event in a new context, escalated from the previous encounter. Establish the new time, place, and circumstance before the exposure begins."
                            : "Continue the current encounter naturally from where it left off.";
                    }
                    else
                    {
                        promptText = "Describe this same moment from your character's perspective.";
                    }
                }
                else
                {
                    promptText = i == 0
                        ? "Continue the scene naturally with the next character response."
                        : "Continue the conversation naturally, building on the previous response.";
                }

                await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
                var interaction = await _continuationService.ContinueAsync(
                    session, actor, actorName, PromptIntent.Message, promptText, onChunk, cancellationToken,
                    turnIndex: persistedTurn.TurnIndex,
                    positionInTurn: positionInTurn,
                    turnActorCount: batchSize);

                result.ParticipantOutputs.Add(interaction);
                // Append to session so next iteration's prompt sees this interaction
                session.Interactions.Add(interaction);
                outputInteractionIds.Add(interaction.Id);
                await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
            }

            // Exclude the time-skip instruction after this turn completes so it does not
            // persist into subsequent turns via "Active Instruction (persistent)" re-injection.
            if (injectedTimeSkipInstruction is not null)
            {
                injectedTimeSkipInstruction.IsExcluded = true;
            }
        }
        else
        {
            // Fallback: single actor default
            var fallbackActor = ResolveDefaultContinueActor(session);
            var fallbackActorName = ResolveActorName(fallbackActor, request.CustomIdentityName);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var interaction = await _continuationService.ContinueAsync(
                session,
                fallbackActor,
                fallbackActorName,
                PromptIntent.Message,
                "Continue naturally with the next interaction that best fits recent context.",
                onChunk,
                cancellationToken);

            result.ParticipantOutputs.Add(interaction);
            session.Interactions.Add(interaction);
            outputInteractionIds.Add(interaction.Id);
            await UpdateStateAndDetectEncounterAsync(session, interaction, cancellationToken);
        }

        // --- AUTO-NARRATIVE ---
        // Include narrative if explicitly requested OR if AutoNarrative is on for overflow continues
        // Skip if we already generated the opening narrative above
        var suppressNarrativeForDecisionTurn = session.SuppressNextNarrativeAfterDecision;
        if (suppressNarrativeForDecisionTurn)
        {
            session.SuppressNextNarrativeAfterDecision = false;
        }

        var shouldIncludeNarrative = !isOpeningScene
            && !suppressNarrativeForDecisionTurn
            && (request.IncludeNarrative
                || (isOverflowContinue && session.AutoNarrative && ShouldAutoNarrate(session)));

        if (suppressNarrativeForDecisionTurn && _debugEventSink is not null)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "DecisionPostTurnNarrativeSuppressed",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName,
                Summary = "Narrative was suppressed for the immediate post-decision continuation turn.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    source = request.TriggeredBy.ToString(),
                    includeNarrativeRequested = request.IncludeNarrative,
                    isOverflowContinue
                })
            }, cancellationToken);
        }

        if (shouldIncludeNarrative)
        {
            var narrativePrompt = DetermineNarrativePrompt(session);
            await AlignPromptNarrativeStateWithV2Async(session, cancellationToken);
            var narrative = await _continuationService.ContinueNarrativeAsync(
                session,
                "Narrative",
                narrativePrompt,
                cancellationToken,
                turnIndex: persistedTurn.TurnIndex,
                turnActorCount: turnActorCount);
            result.NarrativeOutput = narrative;
            session.Interactions.Add(narrative);
            outputInteractionIds.Add(narrative.Id);
            await UpdateStateAndDetectEncounterAsync(session, narrative, cancellationToken);
        }

        // --- TURN-TAKING ENFORCEMENT ---
        // Count consecutive NPC turns and signal user turn if threshold reached
        UpdateTurnTracking(session, result);

        session.Status = RolePlaySessionStatus.InProgress;
        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continueas-generated");

        _logger.LogInformation(
            "Continue As executed for session {SessionId}: participants={ParticipantCount}, includeNarrative={IncludeNarrative}, source={Source}, isUserTurn={IsUserTurn}",
            session.Id,
            result.ParticipantOutputs.Count,
            shouldIncludeNarrative,
            request.TriggeredBy,
            result.IsUserTurn);

        await RunRolePlayV2PipelinesAsync(session, DecisionTrigger.InteractionStart, cancellationToken);
        // Flush the pending session save to DB before enqueueing semantic analysis jobs.
        // The job handler loads the session from DB; if we only queue the save (debounced 1s),
        // the background runner will read a stale snapshot that does not contain the new interactions.
        await _autoSaveCoordinator.FlushAsync(cancellationToken);
        foreach (var generatedInteraction in outputInteractionIds
            .Select(id => session.Interactions.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x is not null)
            .Cast<RolePlayInteraction>())
        {
            await QueueSemanticInteractionAnalysisAsync(session, generatedInteraction, cancellationToken);
        }
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-continueas-v2-processed");

        if (session.AdaptiveState.IsStateDirty)
        {
            await _stateRepository.SaveAdaptiveStateAsync(session.AdaptiveState, cancellationToken);
            session.AdaptiveState.IsStateDirty = false;
        }
        await _stateRepository.CompleteTurnAsync(
            session.Id,
            persistedTurn.TurnId,
            outputInteractionIds,
            succeeded: true,
            cancellationToken: cancellationToken);

        return result;
    }

    private async Task QueueSemanticInteractionAnalysisAsync(RolePlaySession session, RolePlayInteraction interaction, CancellationToken cancellationToken)
    {
        if (!_enableSemanticInference)
        {
            // Feature flag RolePlayFeatureFlags:EnableSemanticInference is false.
            // Do not create an analysis state row and do not enqueue a background job.
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "SemanticInferenceSkipped",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Semantic inference skipped (RolePlayFeatureFlags:EnableSemanticInference=false).",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interactionId = interaction.Id,
                    reasonCode = "semantic_inference_disabled_by_flag"
                })
            }, cancellationToken);
            return;
        }

        if (_backgroundJobQueue is null || _semanticInteractionAnalysisRepository is null)
        {
            return;
        }

        var sessionId = session.Id;
        if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(interaction.Id))
        {
            return;
        }

        // Fail-fast: required semantic configuration must be present BEFORE we create any
        // analysis state row or enqueue a background job. Per repo no-fallback rule, missing RP
        // configuration is surfaced explicitly to the caller instead of being recorded as an
        // errored background job after the fact. The authoritative theme source is the
        // per-session SessionThemeSelections list (the profile is only a seed at create time).
        var hasSessionThemes = (session.SessionThemeSelections ?? [])
            .Any(x => !string.IsNullOrWhiteSpace(x.ThemeId));
        if (!hasSessionThemes)
        {
            throw new InvalidOperationException(
                $"MissingSemanticConfiguration: session '{sessionId}' has no SessionThemeSelections; " +
                "cannot enqueue semantic interaction analysis. Add themes to the session.");
        }

        if (session.ContextWindowSize <= 0)
        {
            throw new InvalidOperationException(
                $"MissingSemanticConfiguration: session '{sessionId}' ContextWindowSize must be greater than zero; " +
                "cannot enqueue semantic interaction analysis.");
        }

        // Enqueue the newly generated interaction, then catch up any prior interactions that do not
        // yet have a Complete analysis row (e.g. created before the profile was set, or previously
        // failed and since reset). The in-memory queue dedupes by (sessionId:interactionId) so this
        // is safe to call repeatedly.
        await EnqueueSingleAnalysisAsync(sessionId, interaction, cancellationToken);

        var priorStates = await _semanticInteractionAnalysisRepository.ListBySessionAsync(sessionId, cancellationToken);
        var completeOrAnalyzing = priorStates
            .Where(s => s.Status == SemanticAnalysisStatus.Complete || s.Status == SemanticAnalysisStatus.Analyzing)
            .Select(s => s.InteractionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var priorInteraction in session.Interactions)
        {
            if (string.IsNullOrWhiteSpace(priorInteraction.Id)) continue;
            if (string.Equals(priorInteraction.Id, interaction.Id, StringComparison.OrdinalIgnoreCase)) continue;
            if (priorInteraction.IsExcluded) continue;
            if (completeOrAnalyzing.Contains(priorInteraction.Id)) continue;

            await EnqueueSingleAnalysisAsync(sessionId, priorInteraction, cancellationToken);
        }
    }

    private async Task EnqueueSingleAnalysisAsync(string sessionId, RolePlayInteraction interaction, CancellationToken cancellationToken)
    {
        // Semantic analysis applies only to character/persona interactions.
        // System-type interactions (Narrative) do not represent a character speaking
        // and must not be fed to the semantic inference model.
        if (interaction.InteractionType == InteractionType.System)
        {
            return;
        }

        var characterId = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName.Trim();

        await _semanticInteractionAnalysisRepository!.UpsertAsync(new SemanticInteractionAnalysisState
        {
            SessionId = sessionId,
            InteractionId = interaction.Id,
            CharacterId = characterId,
            Status = SemanticAnalysisStatus.Idle,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        }, cancellationToken);

        var payloadJson = JsonSerializer.Serialize(new SemanticInteractionAnalysisJobPayload
        {
            SessionId = sessionId,
            InteractionId = interaction.Id,
            CharacterId = characterId
        });

        _backgroundJobQueue!.Enqueue(
            BackgroundJobTypes.SemanticInteractionAnalysis,
            payloadJson,
            dedupeKey: $"{sessionId}:{interaction.Id}");
    }

    public async Task<RolePlayPendingDecisionPrompt?> GetPendingDecisionPromptAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 30, cancellationToken);
        if (points.Count == 0)
        {
            return null;
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        var deferredIds = session.DeferredDecisionPointIds ??= [];
        var pending = points
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefault(x =>
                !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase)
                && !deferredIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase));
        if (pending is null)
        {
            return null;
        }

        var options = await _stateRepository.LoadDecisionOptionsAsync(pending.DecisionPointId, cancellationToken);
        options = ApplyTransparencyToDecisionOptions(options, pending.TransparencyMode);
        return new RolePlayPendingDecisionPrompt
        {
            DecisionPoint = pending,
            Options = options
        };
    }

    public async Task<IReadOnlyList<RolePlayPendingDecisionPrompt>> GetDeferredDecisionPromptsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return [];
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 60, cancellationToken);
        if (points.Count == 0)
        {
            return [];
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        var deferredIds = session.DeferredDecisionPointIds ??= [];
        if (deferredIds.Count == 0)
        {
            return [];
        }

        var deferredPoints = points
            .Where(x =>
                deferredIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase)
                && !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedUtc)
            .ToList();

        var prompts = new List<RolePlayPendingDecisionPrompt>(deferredPoints.Count);
        foreach (var point in deferredPoints)
        {
            var options = await _stateRepository.LoadDecisionOptionsAsync(point.DecisionPointId, cancellationToken);
            options = ApplyTransparencyToDecisionOptions(options, point.TransparencyMode);
            prompts.Add(new RolePlayPendingDecisionPrompt
            {
                DecisionPoint = point,
                Options = options
            });
        }

        return prompts;
    }

    public async Task<DecisionOutcome?> ApplyDecisionAsync(
        string sessionId,
        string decisionPointId,
        string optionId,
        string? customResponseText = null,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null)
        {
            return null;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var state = MapToV2State(session);
        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        var askingActorId = !string.IsNullOrWhiteSpace(decisionPoint?.AskingActorName)
            ? decisionPoint!.AskingActorName
            : ResolveDecisionActorId(state, session, session.PersonaName);
        var targetActorId = !string.IsNullOrWhiteSpace(decisionPoint?.TargetActorId)
            ? decisionPoint!.TargetActorId
            : ResolveDecisionTargetActorId(state, askingActorId);
        var responderActorId = !string.IsNullOrWhiteSpace(targetActorId)
            ? targetActorId
            : askingActorId;
        var outcome = await _decisionPointService.ApplyDecisionAsync(
            state,
            new DecisionSubmission
            {
                DecisionPointId = decisionPointId,
                OptionId = optionId,
                CustomResponseText = customResponseText,
                ActorName = string.IsNullOrWhiteSpace(responderActorId)
                    ? (string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName)
                    : responderActorId,
                TargetActorId = targetActorId
            },
            targetActorId,
            cancellationToken);

        if (!outcome.Applied)
        {
            return outcome;
        }

        ApplyDecisionOutcomeToSessionState(session, outcome);
        session.AppliedDecisionPointIds ??= [];
        if (!session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.AppliedDecisionPointIds.Add(decisionPointId);
        }

        session.DeferredDecisionPointIds ??= [];
        session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase));
        session.SuppressNextNarrativeAfterDecision = _suppressNarrativeAfterDecision;

        var selectedOption = await ResolveAppliedDecisionOptionAsync(decisionPointId, optionId, cancellationToken);
        var (selectedDialogue, selectedDialogueSource) = ResolveSelectedDecisionDialogueWithSource(selectedOption, customResponseText);
        if (string.IsNullOrWhiteSpace(selectedDialogue))
        {
            selectedDialogue = ResolveFallbackDecisionDialogue(optionId);
            selectedDialogueSource = "fallback-option-label";
        }

        var steeringInstruction = BuildDecisionSteeringInstruction(selectedDialogue);
        if (!string.IsNullOrWhiteSpace(steeringInstruction))
        {
            var instructionActorName = BuildDecisionInstructionActorName(session, targetActorId);
            session.Interactions.Add(new RolePlayInteraction
            {
                InteractionType = InteractionType.System,
                ActorName = instructionActorName,
                Content = steeringInstruction
            });

            if (_debugEventSink is not null)
            {
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "DecisionInstructionInjected",
                    Severity = "Info",
                    ActorName = instructionActorName,
                    Summary = $"Decision instruction injected for {optionId}.",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        decisionPointId,
                        optionId,
                        trigger = decisionPoint?.TriggerSource,
                        targetActorId,
                        askingActorId,
                        selectedDialogue,
                        selectedDialogueSource,
                        injectedInstruction = steeringInstruction,
                        responsePreview = selectedOption?.ResponsePreview,
                        displayText = selectedOption?.DisplayText
                    })
                }, cancellationToken);
            }
        }
        else if (_debugEventSink is not null)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "DecisionInstructionSkipped",
                Severity = "Warning",
                ActorName = ResolveLocationActorLabel(session, targetActorId),
                Summary = $"Decision instruction skipped for {optionId}: no dialogue resolved.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    decisionPointId,
                    optionId,
                    trigger = decisionPoint?.TriggerSource,
                    targetActorId,
                    askingActorId,
                    selectedDialogue,
                    selectedDialogueSource,
                    responsePreview = selectedOption?.ResponsePreview,
                    displayText = selectedOption?.DisplayText
                })
            }, cancellationToken);
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-applied");
        return outcome;
    }

    public async Task<bool> DeferDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        if (decisionPoint is null)
        {
            return false;
        }

        session.AppliedDecisionPointIds ??= [];
        if (session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        session.DeferredDecisionPointIds ??= [];
        if (!session.DeferredDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.DeferredDecisionPointIds.Add(decisionPointId);
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-deferred");
        return true;
    }

    public async Task<bool> RestoreDeferredDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        session.DeferredDecisionPointIds ??= [];
        var removed = session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
        {
            return false;
        }

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-restored");
        return true;
    }

    public async Task<bool> SkipDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(sessionId, cancellationToken);
        if (session is null || string.IsNullOrWhiteSpace(decisionPointId))
        {
            return false;
        }

        await ValidateSessionCompatibilityOrThrowAsync(session, cancellationToken);

        var decisionPoint = await ResolveDecisionPointAsync(sessionId, decisionPointId, cancellationToken);
        if (decisionPoint is null)
        {
            return false;
        }

        session.AppliedDecisionPointIds ??= [];
        if (!session.AppliedDecisionPointIds.Contains(decisionPointId, StringComparer.OrdinalIgnoreCase))
        {
            session.AppliedDecisionPointIds.Add(decisionPointId);
        }

        session.DeferredDecisionPointIds ??= [];
        session.DeferredDecisionPointIds.RemoveAll(x => string.Equals(x, decisionPointId, StringComparison.OrdinalIgnoreCase));

        session.ModifiedAt = DateTime.UtcNow;
        _autoSaveCoordinator.QueueRolePlaySessionSave(session, "roleplay-decision-skipped");
        return true;
    }

    /// <summary>
    /// Resolves the scene characters that should naturally continue the conversation.
    /// Looks at the scenario character list and recent interaction history to pick
    /// the most relevant characters in a natural conversation order.
    /// </summary>
    private async Task<List<OverflowActorCandidate>> ResolveSceneContinueActorsAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var actors = new List<OverflowActorCandidate>();
        var autoAllowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode, explicitSelection: false).ToHashSet();

        // Gather scenario characters.
        var sceneCharacterNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                foreach (var character in scenario.Characters)
                {
                    if (!string.IsNullOrWhiteSpace(character.Name))
                    {
                        sceneCharacterNames.Add(character.Name.Trim());
                    }
                }
            }
        }

        if (sceneCharacterNames.Count == 0)
        {
            foreach (var perspective in session.CharacterPerspectives)
            {
                if (!string.IsNullOrWhiteSpace(perspective.CharacterName))
                {
                    sceneCharacterNames.Add(perspective.CharacterName.Trim());
                }
            }
        }

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        sceneCharacterNames = sceneCharacterNames
            .Where(name => !string.Equals(name, personaName, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sceneCharacterNames.Count == 0 && !autoAllowedActors.Contains(ContinueAsActor.You))
        {
            // No scenario characters � fall back to default single actor
            var fallback = ResolveDefaultContinueActor(session);
            actors.Add(new OverflowActorCandidate(
                fallback,
                ResolveActorName(fallback, null),
                "Fallback actor because no scenario characters were available for automatic continuation."));
            return actors;
        }

        // Determine conversation order by recency: characters who haven't spoken recently,
        // or never spoke at all, go first.
        var recentActors = session.Interactions
            .Where(i => (i.InteractionType == InteractionType.Npc || i.InteractionType == InteractionType.Custom) && !i.IsExcluded)
            .TakeLast(6)
            .Select(i => i.ActorName?.Trim())
            .ToList();

        var currentSceneLocation = _enableLocationServices
            ? session.AdaptiveState.CurrentSceneLocation
            : null;

        // B-049: Count all non-system interactions (Npc, You, Custom) to decide whether OtherMan is eligible.
        // Using only Npc type caused a chicken-and-egg: Dean never got turns so the count never reached threshold.
        var totalInteractions = session.Interactions.Count(i =>
            i.InteractionType != InteractionType.System && !i.IsExcluded);

        // Hard-exclude OtherMan for the first 6 interactions so Husband+Wife establish first.
        // With batchSize=2 (Ken+Becky per turn), 3 turns = 6 interactions ? Dean eligible on turn 4.
        var eligibleCharacterNames = sceneCharacterNames.Where(name =>
        {
            session.AdaptiveState.CharacterStats.TryGetValue(name, out var statProfile);
            var role = statProfile?.CharacterRole;
            if (totalInteractions < 6 && string.Equals(role, "OtherMan", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug(
                    "OverflowActor: excluding OtherMan {ActorName} at interaction offset {Offset} for SessionId={SessionId}",
                    name, totalInteractions, session.Id);
                return false;
            }
            return true;
        }).ToList();

        var ordered = eligibleCharacterNames
            .Select((name, scenarioOrder) => new
            {
                Name = name,
                ScenarioOrder = scenarioOrder,
                LastSeenIndex = recentActors.FindLastIndex(actorName => string.Equals(actorName, name, StringComparison.OrdinalIgnoreCase)),
                InScene = IsActorInCurrentScene(session, name, currentSceneLocation)
            })
            .OrderByDescending(x => x.InScene)
            .ThenBy(x => x.LastSeenIndex < 0 ? int.MinValue : x.LastSeenIndex)
            .ThenBy(x => x.ScenarioOrder)
            .Select(x => x.Name)
            .ToList();

        if (autoAllowedActors.Contains(ContinueAsActor.Npc))
        {
            foreach (var name in ordered)
            {
                var inScene = IsActorInCurrentScene(session, name, currentSceneLocation);
                var recencyIndex = recentActors.FindLastIndex(actorName => string.Equals(actorName, name, StringComparison.OrdinalIgnoreCase));
                var recencyReason = recencyIndex < 0 ? "not recently active" : $"recent-index={recencyIndex}";
                var sceneReason = inScene ? "in-scene" : "out-of-scene";
                actors.Add(new OverflowActorCandidate(
                    ContinueAsActor.Npc,
                    name,
                    $"NPC auto candidate ({sceneReason}, {recencyReason})."));
            }
        }

        if (autoAllowedActors.Contains(ContinueAsActor.You))
        {
            // Persona is always a candidate � treated as a full character in the rotation.
            var personaInScene = IsActorInCurrentScene(session, personaName, currentSceneLocation);
            var personaReason = personaInScene
                ? "Persona auto candidate (in-scene)."
                : "Persona auto candidate (not in scene but always included).";

            if (totalInteractions < 6)
            {
                // Initial setup: persona leads to establish husband-wife dynamic
                actors.Insert(0, new OverflowActorCandidate(ContinueAsActor.You, personaName,
                    "Persona auto candidate (initial lead for scenario setup)."));
            }
            else
            {
                // Persona every other turn to reduce repetition when oblivious.
                // Even ObservedTurnCount ? include; odd ? skip (narrative still closes turn).
                var includePersona = session.AdaptiveState.ObservedTurnCount % 2 == 0;
                if (includePersona)
                {
                    actors.Add(new OverflowActorCandidate(ContinueAsActor.You, personaName,
                        "Persona auto candidate (last before narrative, even turn)."));
                }
                else
                {
                    _logger.LogDebug(
                        "OverflowActor: skipping persona {PersonaName} on turn {TurnCount} for SessionId={SessionId}",
                        personaName, session.AdaptiveState.ObservedTurnCount, session.Id);
                }
            }
        }

        if (actors.Count == 0)
        {
            var fallback = ResolveDefaultContinueActor(session);
            var fallbackName = fallback == ContinueAsActor.You ? personaName : ResolveActorName(fallback, null);
            actors.Add(new OverflowActorCandidate(
                fallback,
                fallbackName,
                "Fallback actor because automatic candidate list was empty after mode filtering."));
        }

        return actors;
    }

    private static bool IsActorInCurrentScene(RolePlaySession session, string actorName, string? currentSceneLocation)
    {
        if (string.IsNullOrWhiteSpace(actorName) || string.IsNullOrWhiteSpace(currentSceneLocation))
        {
            return false;
        }

        var location = session.AdaptiveState.CharacterLocations.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(x.CharacterId)
            && string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));

        if (location is null || string.IsNullOrWhiteSpace(location.TrueLocation))
        {
            return false;
        }

        return string.Equals(location.TrueLocation.Trim(), currentSceneLocation.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludePersonaInAutoRotation(RolePlaySession session, string personaName, string? currentSceneLocation)
    {
        var personaInScene = IsActorInCurrentScene(session, personaName, currentSceneLocation);
        var recent = session.Interactions
            .Where(x => !x.IsExcluded && x.InteractionType != InteractionType.System)
            .TakeLast(4)
            .ToList();

        if (recent.Count == 0)
        {
            return personaInScene;
        }

        var personaSpokeVeryRecently = recent.TakeLast(2).Any(x =>
            x.InteractionType == InteractionType.User
            || string.Equals(x.ActorName, personaName, StringComparison.OrdinalIgnoreCase));
        if (personaSpokeVeryRecently)
        {
            return false;
        }

        var npcSpokeRecently = recent.TakeLast(2).Any(x => x.InteractionType is InteractionType.Npc or InteractionType.Custom);
        if (!npcSpokeRecently)
        {
            return false;
        }

        // Permit occasional out-of-scene persona turns, but bias toward in-scene inclusion.
        return personaInScene || recent.Count >= 3;
    }

    private sealed record OverflowActorCandidate(ContinueAsActor Actor, string Name, string Reason);

    private async Task RebuildAdaptiveStateInternalAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        // Preserve encounter profile selections before discarding adaptive state �
        // CharacterEncounterProfileIds is stored on AdaptiveState and would be lost on reset.
        var savedEncounterProfileIds = new Dictionary<string, string>(
            session.AdaptiveState.CharacterEncounterProfileIds, StringComparer.OrdinalIgnoreCase);

        session.AdaptiveState = new AdaptiveScenarioState();

        // Restore so SeedRuntimeEncounterStatsAsync can seed behavioral stats from the original profiles.
        foreach (var kvp in savedEncounterProfileIds)
            session.AdaptiveState.CharacterEncounterProfileIds[kvp.Key] = kvp.Value;

        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null)
            {
                await SeedAdaptiveStateFromScenarioAsync(session, scenario, cancellationToken);
                await SeedRuntimeEncounterStatsAsync(session, scenario, cancellationToken);
            }
        }

        foreach (var interaction in session.Interactions.Where(x => !x.IsExcluded))
        {
            session.AdaptiveState = await UpdateAdaptiveStateWithSemanticDiagnosticsAsync(session, interaction, cancellationToken);
        }

        _logger.LogInformation(
            "Adaptive state rebuilt for session {SessionId}: interactionsReplayed={InteractionCount}, primaryTheme={PrimaryTheme}, secondaryTheme={SecondaryTheme}",
            session.Id,
            session.Interactions.Count(x => !x.IsExcluded),
            session.AdaptiveState.PrimaryThemeId ?? "(none)",
            session.AdaptiveState.SecondaryThemeId ?? "(none)");
    }


    private async Task UpdateStateAndDetectEncounterAsync(RolePlaySession session, RolePlayInteraction interaction, CancellationToken cancellationToken)
    {
        session.AdaptiveState = await UpdateAdaptiveStateWithSemanticDiagnosticsAsync(session, interaction, cancellationToken);

        // Multi-encounter Climax: per-interaction counter increment.
        // The pipeline-scoped increment in RunRolePlayV2PipelinesAsync is additive
        // but may not fire every turn; this ensures the running encounter count is
        // always current when TryDetectEncounterBoundaryAsync inspects it.
        if (session.AdaptiveState.CurrentEncounterNumber > 0
            && session.AdaptiveState.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax)
        {
            session.AdaptiveState.InteractionsInCurrentEncounter++;
        }

        // ---- Encounter participation tracking (Change 1: sync heuristic) -----------------
        // Only track character actors (Npc, User, Custom) — System interactions
        // (Narrative, Instruction) describe the scene but are not encounter participants.
        var actorName = interaction.ActorName;
        if (!string.IsNullOrWhiteSpace(actorName)
            && interaction.InteractionType != InteractionType.System
            && HasSexualActivityContent(interaction.Content))
        {
            if (!session.AdaptiveState.CharacterEncounterStates.TryGetValue(actorName, out var encState))
            {
                encState = new CharacterEncounterState();
                session.AdaptiveState.CharacterEncounterStates[actorName] = encState;
            }
            encState.IsHavingSex = true;
            encState.EncounterNumber = session.AdaptiveState.CurrentEncounterNumber;
            encState.EnteredEncounterUtc ??= DateTime.UtcNow;
        }

        await TryDetectEncounterBoundaryAsync(session, interaction, session.AdaptiveState, cancellationToken);
    }

    private async Task<AdaptiveScenarioState> UpdateAdaptiveStateWithSemanticDiagnosticsAsync(
        RolePlaySession session,
        RolePlayInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (!_enableAdaptiveStateUpdates)
        {
            // ? DO NOT "FIX" THIS � EnableAdaptiveStateUpdates=false IS INTENTIONAL AND PERMANENT.
            // The old inline keyword/regex adaptive-state path was REPLACED by the Semantic Engine.
            // All stat deltas now come from the async Semantic Engine pipeline (SemanticInferredEvidenceApplied
            // events via ApplyInferredSemanticEvidenceAsync ? ApplySemanticEvidenceAsync).
            // AdaptiveStateUpdateSkipped in the debug log is EXPECTED. It is not a bug.
            // If RuntimeEncounterStats are not seeding, the bug is in ApplySemanticEvidenceAsync,
            // not here. Do not change this flag.
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "AdaptiveStateUpdateSkipped",
                Severity = "Info",
                ActorName = interaction.ActorName,
                Summary = "Adaptive state update skipped (RolePlayFeatureFlags:EnableAdaptiveStateUpdates=false).",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interactionId = interaction.Id,
                    reasonCode = "adaptive_state_disabled_by_flag"
                })
            }, cancellationToken);
            await _adaptiveStateService.EvaluateAdaptiveIntensityTransitionAsync(session, interaction, cancellationToken);
            return session.AdaptiveState;
        }

        try
        {
            var updatedState = await _adaptiveStateService.UpdateFromInteractionAsync(session, interaction, cancellationToken);

            // Note: this sync path runs ApplySemanticEvidenceAsync with inferredSignals=null and
            // only sees inline SemanticSignalRegex matches in interaction content. The real
            // semantic contribution is applied asynchronously by SemanticInteractionAnalysisJobHandler
            // via ApplyInferredSemanticEvidenceAsync after the LLM inference job completes, which
            // emits its own SemanticInferredEvidenceApplied debug event. Do not emit a
            // "no contribution" diagnostic here because it would race the async job and mislead.
            await _adaptiveStateService.EvaluateAdaptiveIntensityTransitionAsync(session, interaction, cancellationToken);

            return updatedState;
        }
        catch (InvalidOperationException ex)
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                InteractionId = interaction.Id,
                EventKind = "SemanticProcessingFailed",
                Severity = "Error",
                ActorName = interaction.ActorName,
                Summary = "Semantic processing failed with explicit diagnostics.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    interactionId = interaction.Id,
                    error = ex.Message,
                    reasonCode = ex.Message.Split(':', 2, StringSplitOptions.TrimEntries)[0]
                })
            }, cancellationToken);

            throw;
        }
    }

    private async Task SeedAdaptiveStateFromScenarioAsync(RolePlaySession session, DreamGenClone.Web.Domain.Scenarios.Scenario scenario, CancellationToken cancellationToken)
    {
        var resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(scenario.ResolvedBaseStats);
        if (!string.IsNullOrWhiteSpace(scenario.BaseStatProfileId))
        {
            var baseStatProfile = await _characterProfileService.GetAsync(scenario.BaseStatProfileId, cancellationToken);
            if (baseStatProfile is not null)
            {
                resolvedBaseStats = AdaptiveStatCatalog.NormalizeComplete(baseStatProfile.CharacterStats);
                scenario.ResolvedBaseStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
            }
        }

        session.CharacterPerspectives = scenario.Characters
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => new RolePlayCharacterPerspective
            {
                CharacterId = x.Id,
                CharacterName = x.Name!.Trim(),
                PerspectiveMode = x.PerspectiveMode
            })
            .ToList();

        foreach (var character in scenario.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name))
            {
                continue;
            }

            var mergedStats = new Dictionary<string, int>(resolvedBaseStats, StringComparer.OrdinalIgnoreCase);
            var normalizedCharacterOverrides = AdaptiveStatCatalog.NormalizePartial(character.BaseStats);
            foreach (var (statName, statValue) in normalizedCharacterOverrides)
            {
                mergedStats[statName] = statValue;
            }

            if (mergedStats.Count == 0)
            {
                continue;
            }

            var seededProfile = CharacterStatProfileV2Accessor.CreateDefault(character.Id);
            CharacterStatProfileV2Accessor.SetAllStats(seededProfile, mergedStats);
            seededProfile.BaselineStats = new Dictionary<string, int>(mergedStats, StringComparer.OrdinalIgnoreCase);
            session.AdaptiveState.CharacterStats[character.Name.Trim()] = seededProfile;
        }

        await _adaptiveStateService.SeedFromScenarioAsync(session, scenario, cancellationToken);
    }

    /// <summary>
    /// Seeds RuntimeEncounterStats for all characters and persona in a session from the encounter profiles
    /// stored in session.AdaptiveState.CharacterEncounterProfileIds. Called after SeedAdaptiveStateFromScenarioAsync
    /// during both session creation (via CreateSessionAsync) and on rebuild so stats survive Scenario Save.
    /// Characters without a selected encounter profile are seeded at neutral 50 per dimension.
    /// </summary>
    private async Task SeedRuntimeEncounterStatsAsync(
        RolePlaySession session,
        DreamGenClone.Web.Domain.Scenarios.Scenario scenario,
        CancellationToken cancellationToken)
    {
        var encounterProfileIds = session.AdaptiveState.CharacterEncounterProfileIds;

        foreach (var character in scenario.Characters)
        {
            if (string.IsNullOrWhiteSpace(character.Name)) continue;
            var charKey = character.Name.Trim();
            if (!session.AdaptiveState.CharacterStats.TryGetValue(charKey, out var block)) continue;

            if (encounterProfileIds.TryGetValue(character.Id, out var encProfileId)
                && !string.IsNullOrWhiteSpace(encProfileId))
            {
                var dims = BehavioralDimensionCatalog.GetDimensions(block.CharacterRole ?? string.Empty);
                if (dims.Count > 0)
                {
                    var encProfile = await _characterProfileService.GetAsync(encProfileId, cancellationToken);
                    if (encProfile?.EncounterStats is { Count: > 0 })
                    {
                        block.RuntimeEncounterStats = dims.ToDictionary(
                            d => d.Name,
                            d => encProfile.EncounterStats.TryGetValue(d.Name, out var v) ? v : 50,
                            StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
        }

        // Persona
        if (encounterProfileIds.TryGetValue("__persona__", out var personaEncProfileId)
            && !string.IsNullOrWhiteSpace(personaEncProfileId)
            && !string.IsNullOrWhiteSpace(session.PersonaName)
            && session.AdaptiveState.CharacterStats.TryGetValue(session.PersonaName, out var personaBlock))
        {
            var personaDims = BehavioralDimensionCatalog.GetDimensions(personaBlock.CharacterRole ?? string.Empty);
            if (personaDims.Count > 0)
            {
                var personaEncProfile = await _characterProfileService.GetAsync(personaEncProfileId, cancellationToken);
                if (personaEncProfile?.EncounterStats is { Count: > 0 })
                {
                    personaBlock.RuntimeEncounterStats = personaDims.ToDictionary(
                        d => d.Name,
                        d => personaEncProfile.EncounterStats.TryGetValue(d.Name, out var v) ? v : 50,
                        StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        // Seed any remaining characters at neutral 50 (no encounter profile selected).
        foreach (var block in session.AdaptiveState.CharacterStats.Values)
        {
            if (block.RuntimeEncounterStats is { Count: > 0 } || string.IsNullOrWhiteSpace(block.CharacterRole))
                continue;
            var dims = BehavioralDimensionCatalog.GetDimensions(block.CharacterRole);
            if (dims.Count > 0)
                block.RuntimeEncounterStats = dims.ToDictionary(d => d.Name, _ => 50, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Determines whether auto-narrative should fire based on recent interaction patterns.
    /// Narrative is warranted after user actions, scene transitions, or long character-only exchanges.
    /// </summary>
    private static bool ShouldAutoNarrate(RolePlaySession session)
    {
        var recent = session.Interactions
            .Where(i => !i.IsExcluded)
            .TakeLast(6)
            .ToList();

        if (recent.Count == 0)
            return true; // Opening scene � always narrate

        var lastInteraction = recent[^1];

        // Narrate after a user action (Ken stepped away ? describe what happens)
        if (lastInteraction.InteractionType == InteractionType.User)
            return true;

        // Narrate after an instruction (scene direction ? describe the result)
        if (lastInteraction.InteractionType == InteractionType.System && lastInteraction.ActorName == "Instruction")
            return true;

        // Count consecutive character messages without narrative.
        // Include User-type interactions (persona auto-generated in a batch) � these are not
        // manual user submissions (that case is caught above when lastInteraction.User ? return true).
        // Only stop at System interactions (Narrative, Instruction, etc.).
        var consecutiveMessages = 0;
        for (var i = recent.Count - 1; i >= 0; i--)
        {
            if (recent[i].InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.User)
                consecutiveMessages++;
            else
                break;
        }

        // Insert narrative after 2+ character messages without one
        return consecutiveMessages >= 2;
    }

    private async Task<string> BuildOpeningNarrativePromptAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        // B-049: Opening scene uses the data model to identify the natural couple
        // (persona + spouse character) without hardcoding names or roles.
        // The opening is exclusively about the persona and their partner � 300�500 words,
        // drawing on their character profiles for history, dynamic, and tonal foreshadowing.
        // Other characters remain peripheral and unnamed throughout the opening.
        const string basePrompt =
            "Write the opening narrative for this scene. " +
            "This opening is exclusively about the persona and their partner � focus entirely on their interaction with each other. " +
            "Describe what the persona is doing, what their partner observes about them, their immediate environment and atmosphere, " +
            "and any history between them that can be inferred from their character profiles. " +
            "Include the physical and emotional dynamic between them � whether it feels passionate, familiar, routine, or quietly strained � " +
            "drawing from their personalities, backgrounds, and histories as described in their profiles. " +
            "Weave in subtle, tonal foreshadowing through body language, atmosphere, and emotional texture. Do not state any subtext explicitly. " +
            "Other characters may be present in the scene but must remain peripheral background presence only. " +
            "Do not refer to them by name or bring them into any character's attention, thoughts, or dialogue. " +
            "Write 300�500 words.";

        if (string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            _logger.LogDebug("BuildOpeningNarrative: no scenario, using base prompt for SessionId={SessionId}", session.Id);
            return basePrompt + " In the opening paragraph, ground the scene in a specific, clear location.";
        }

        var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);

        // Use the first scenario Opening when available � this provides data-driven
        // contextual guidance for the opening narrative (e.g. "arriving at the party
        // and putting things away in the guest room"). When no Opening is defined,
        // fall back to listing all location names for the model to choose from.
        string? openingText = scenario?.Openings
            ?.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Text))
            ?.Text?.Trim();

        var locationNames = scenario?.Locations
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList() ?? [];

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        var npcCharacters = scenario?.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToList() ?? [];

        // Resolve the spouse character from the data model: the NPC whose RelationTargetId
        // points to the persona (or whose Role is the "partner" role to the persona).
        // A character linked to the persona via RelationTargetId is the persona's spouse.
        var spouseCharacter = npcCharacters.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(c.RelationTargetId) &&
            string.Equals(c.RelationTargetId.Trim(), personaName, StringComparison.OrdinalIgnoreCase));

        // Build the couple grounding clause: persona + spouse, if data supports it.
        // This expands the base directive with character-specific context: shared history,
        // physical/emotional dynamic, and layered tonal subtext drawn from their profiles.
        var coupleClause = string.Empty;
        if (spouseCharacter is not null)
        {
            var spouseName = spouseCharacter.Name!.Trim();
            coupleClause =
                $" The scene opens with {personaName} and {spouseName} together." +
                $" {personaName} is the persona character; {spouseName} is their partner." +
                $" Ground the opening in their direct interaction with each other." +
                $" Use both characters' profile descriptions � personalities, backgrounds, physical traits, and histories � to infer their shared history and the texture of their relationship." +
                $" Portray their physical and emotional dynamic authentically from what the profiles suggest: it may be warm, complicated, quietly distant, or something that has simply settled into habit." +
                $" Include their sex life as part of that texture � let the writing convey, through body language, sensory detail, and emotional atmosphere, whether desire between them is alive, faded, or quietly suppressed." +
                $" Do not state any of this explicitly. Let tone, behavior, and physical presence carry the subtext.";
            _logger.LogDebug(
                "BuildOpeningNarrative: couple guidance for SessionId={SessionId}, Persona={PersonaName}, Spouse={SpouseName}",
                session.Id, personaName, spouseName);
        }
        else
        {
            _logger.LogDebug(
                "BuildOpeningNarrative: no relation-target spouse found for SessionId={SessionId}, Persona={PersonaName}",
                session.Id, personaName);
        }

        // Build scenario context block: Plot Description, World Description, Time Frame.
        // This gives the model the essential scenario setting context that the regular
        // continuation prompt (BuildPromptAsync) also injects on every turn.
        var scenarioContext = string.Empty;
        if (scenario is not null)
        {
            var ctx = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(scenario.Description))
                ctx.Append($" Setting: {scenario.Description.Trim()}");
            if (!string.IsNullOrWhiteSpace(scenario.Plot?.Description))
                ctx.Append($" Plot: {scenario.Plot.Description.Trim()}");
            if (!string.IsNullOrWhiteSpace(scenario.Setting?.WorldDescription))
                ctx.Append($" World: {scenario.Setting.WorldDescription.Trim()}");
            if (!string.IsNullOrWhiteSpace(scenario.Setting?.TimeFrame))
            {
                ctx.Append($" Time Frame: {scenario.Setting.TimeFrame.Trim()}");
                ctx.Append(" The entire story takes place within this time frame � scenes may skip forward in time; a new response does not have to be the immediate continuation of the last moment.");
            }

            if (ctx.Length > 0)
                scenarioContext = ctx.ToString();
        }

        if (!string.IsNullOrWhiteSpace(openingText))
        {
            // When a scenario Opening is defined, use it as contextual guidance for
            // the opening narrative. The Opening text describes the starting situation
            // (e.g. arriving at the party) and may reference a specific location.
            return basePrompt + coupleClause
                + $"\n\nScenario Context:{scenarioContext}"
                + $"\nOpening: {openingText}"
                + " Ground the opening in the location and situation described above."
                + " Describe the atmosphere, the immediate surroundings, and their interaction."
                + " Remember: other characters may be present at this location but must remain"
                + " peripheral background presence only � do not name them or bring them into"
                + " the characters' focus."
                + " Keep this grounding natural and immersive, not bullet points.";
        }

        if (locationNames.Count == 0)
        {
            return basePrompt + coupleClause
                + $"\n\nScenario Context:{scenarioContext}"
                + " In the opening paragraph, explicitly state where the scene is happening and where key characters are in relation to each other."
                + " Keep this grounding natural and immersive, not bullet points.";
        }

        return basePrompt + coupleClause
            + $"\n\nScenario Context:{scenarioContext}"
            + $" In the first paragraph, explicitly ground the scene in one clear location using one of these names: {string.Join(", ", locationNames)}."
            + " Keep this grounding natural and immersive, not bullet points.";
    }

    /// <summary>
    /// Builds a context-aware narrative prompt based on recent session state.
    /// </summary>
    private static string DetermineNarrativePrompt(RolePlaySession session)
    {
        var lastInteraction = session.Interactions
            .Where(i => !i.IsExcluded)
            .LastOrDefault();

        if (lastInteraction is null)
            return "Set the scene and establish the atmosphere.";

        if (string.Equals(session.AdaptiveState.CurrentPhase.ToString(), "Climax", StringComparison.OrdinalIgnoreCase))
        {
            return "Write an omniscient narrative description of the full scene as it stands this turn. Describe the physical moment, setting, character positions, sensations, and atmosphere in explicit detail. All participants have already described this same moment from their own perspectives � your role is to close the turn with a rich, omniscient account of what is happening right now. Do not advance the scene beyond what the characters have already established. Use direct, explicit language.";
        }

        return lastInteraction.InteractionType switch
        {
            InteractionType.User => $"Describe what happens after {session.PersonaName}'s action. Include scene details, other characters' reactions, internal thoughts, and sensory details.",
            InteractionType.System when lastInteraction.ActorName == "Instruction" => "Follow the instruction. Describe the scene in detail with environment, body language, and atmosphere.",
            _ => "Describe the scene between the characters: body language, internal thoughts, sensory details, and atmosphere. Bridge the dialogue with vivid narrative prose."
        };
    }

    /// <summary>
    /// Updates consecutive NPC turn counter and signals user turn if threshold reached.
    /// </summary>
    private static void UpdateTurnTracking(RolePlaySession session, ContinueAsResult result)
    {
        if (session.BehaviorMode != BehaviorMode.TakeTurns)
            return;

        // Count the NPC outputs just generated
        var npcCount = result.ParticipantOutputs.Count(i => i.InteractionType is InteractionType.Npc or InteractionType.Custom);
        if (result.NarrativeOutput is not null)
            npcCount++;

        session.ConsecutiveNpcTurns += npcCount;

        if (session.ConsecutiveNpcTurns >= session.TurnTakingThreshold)
        {
            session.CurrentTurnState = TurnState.UserTurn;
            result.IsUserTurn = true;
        }
        else
        {
            session.CurrentTurnState = TurnState.NpcTurn;
        }
    }

    private static string BuildContinuationPromptText(PromptIntent intent, string promptText)
    {
        var trimmed = promptText.Trim();
        return intent switch
        {
            PromptIntent.Message => $"Respond in-character and follow this direction for tone/mood/action: {trimmed}",
            PromptIntent.Narrative => $"Expand this into narrative from the selected character POV: {trimmed}",
            _ => trimmed
        };
    }

    private async Task EnsurePersistedSessionsLoadedAsync(CancellationToken cancellationToken)
    {
        var persisted = await _sessionService.GetSessionsByTypeAsync(SessionService.RolePlaySessionType, cancellationToken);
        foreach (var item in persisted)
        {
            if (Sessions.ContainsKey(item.Id))
            {
                continue;
            }

            var loaded = await _sessionService.LoadRolePlaySessionAsync(item.Id, cancellationToken);
            if (loaded is not null)
            {
                if (loaded.Status == RolePlaySessionStatus.NotStarted && loaded.Interactions.Count > 0)
                {
                    loaded.Status = RolePlaySessionStatus.InProgress;
                }

                Sessions.TryAdd(loaded.Id, loaded);
            }
        }
    }

    // Schema compatibility check removed � there is only one session schema now.
    private static Task ValidateSessionCompatibilityOrThrowAsync(RolePlaySession session, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task RunRolePlayV2PipelinesAsync(
        RolePlaySession session,
        DecisionTrigger trigger,
        CancellationToken cancellationToken,
        bool explicitClimaxCompletionRequested = false,
        DreamGenClone.Domain.RolePlay.NarrativePhase? manualPhaseAdvanceTarget = null)
    {
        var previousV2State = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
        var v2State = HydrateV2State(session, previousV2State);

        // Opening → BuildUp: the opening period runs for the first 3 turns with husband-wife
        // guidance. After ObservedTurnCount exceeds OpeningPeriodTurnCount, advance to BuildUp
        // where the observer or theme guidance pipeline takes over.
        // Must run immediately after Hydrate, before any downstream code reads or overwrites CurrentPhase.
        if (v2State.CurrentPhase == NarrativePhase.Opening
            && session.AdaptiveState.ObservedTurnCount > OpeningPeriodTurnCount)
        {
            v2State.CurrentPhase = NarrativePhase.BuildUp;
            v2State.InteractionCountInPhase = 0;
            _logger.LogInformation(
                "RolePlayV2 Opening→BuildUp transition: SessionId={SessionId} ObservedTurnCount={ObservedTurns}",
                session.Id,
                session.AdaptiveState.ObservedTurnCount);
        }

        if (!_enableLocationServices)
        {
            ClearLocationState(v2State);
        }
        NormalizePhaseOverrideLock(v2State);
        var climaxCompletionRequested = explicitClimaxCompletionRequested || IsClimaxCompletionRequested(session);

        // Count actual NPC/narrative interactions generated since the last pipeline evaluation,
        // so batch ContinueAs calls (which may generate 2-3 interactions per button click) advance
        // the counter correctly instead of always adding +1 regardless of batch size.
        var totalGeneratedInteractions = session.Interactions.Count(x =>
            x.InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.System);

        var previousPhaseInteractionCount = Math.Max(0, v2State.InteractionCountInPhase);
        var generatedSinceLastEval = previousV2State?.LastEvaluationUtc is { } lastEval
            ? session.Interactions.Count(x =>
                x.CreatedAt > lastEval
                && x.InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.System)
            : Math.Max(0, totalGeneratedInteractions - previousPhaseInteractionCount);

        var proposedPhaseInteractionCount = previousPhaseInteractionCount + Math.Max(0, generatedSinceLastEval);
        var invariantPhaseInteractionCount = Math.Min(proposedPhaseInteractionCount, totalGeneratedInteractions);
        v2State.InteractionCountInPhase = invariantPhaseInteractionCount;

        if (invariantPhaseInteractionCount != proposedPhaseInteractionCount)
        {
            _logger.LogWarning(
                "RolePlayV2 phase interaction count invariant clamp applied: SessionId={SessionId} PreviousCount={PreviousCount} Delta={Delta} ProposedCount={ProposedCount} ClampedCount={ClampedCount} TotalGeneratedInteractions={TotalGeneratedInteractions} LastEvaluationUtc={LastEvaluationUtc}",
                session.Id,
                previousPhaseInteractionCount,
                generatedSinceLastEval,
                proposedPhaseInteractionCount,
                invariantPhaseInteractionCount,
                totalGeneratedInteractions,
                previousV2State?.LastEvaluationUtc);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "PhaseInteractionCountMismatchDetected",
                Severity = "Warning",
                Summary = "Phase interaction count clamped by invariant to prevent overcount drift.",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    phase = v2State.CurrentPhase.ToString(),
                    previousCount = previousPhaseInteractionCount,
                    delta = generatedSinceLastEval,
                    proposedCount = proposedPhaseInteractionCount,
                    clampedCount = invariantPhaseInteractionCount,
                    totalGeneratedInteractions,
                    lastEvaluationUtc = previousV2State?.LastEvaluationUtc
                })
            }, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
        {
            try
            {
                await EnsureThemeMachineResolutionGuardAsync(session, v2State, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                await PersistThemeMachineFailureDiagnosticAsync(
                    session,
                    v2State,
                    "ThemeMachineResolutionFailure",
                    ex.Message,
                    cancellationToken);
                _logger.LogError(
                    ex,
                    "RolePlayV2 machine resolution failed before candidate evaluation: SessionId={SessionId} ScenarioId={ScenarioId}",
                    session.Id,
                    v2State.ActiveScenarioId);
                throw;
            }
        }

        ReturnBeatAutoDetectionResult returnBeatAutoDetectionResult;
        try
        {
            returnBeatAutoDetectionResult = await TryApplyAutomaticReturnBeatCompletionAsync(session, v2State, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await PersistThemeMachineFailureDiagnosticAsync(
                session,
                v2State,
                "ReturnBeatAutoDetectionFailure",
                ex.Message,
                cancellationToken);
            _logger.LogError(
                ex,
                "RolePlayV2 return-beat auto-detection failed: SessionId={SessionId} ScenarioId={ScenarioId}",
                session.Id,
                v2State.ActiveScenarioId ?? string.Empty);
            throw;
        }

        if (returnBeatAutoDetectionResult.Applied
            && v2State.ThemeMachineSnapshot is not null
            && !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
        {
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "ReturnBeatAutoDetected",
                Severity = "Info",
                Summary = "Return-beat completion auto-detected from narrative signal",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    signal = returnBeatAutoDetectionResult.MatchedSignal,
                    sourceInteractionId = returnBeatAutoDetectionResult.SourceInteractionId,
                    configuredSignalCount = returnBeatAutoDetectionResult.ConfiguredSignalCount,
                    state = v2State.ThemeMachineSnapshot.CurrentStateCode
                })
            }, cancellationToken);

            await _stateRepository.SaveThemeMachineDiagnosticEventsAsync(
            [
                new ThemeMachineDiagnosticEvent
                {
                    SessionId = session.Id,
                    ThemeId = v2State.ActiveScenarioId,
                    MachineKey = v2State.ThemeMachineSnapshot.MachineKey,
                    DefinitionVersion = v2State.ThemeMachineSnapshot.DefinitionVersion,
                    EventType = "signal",
                    FromStateCode = v2State.ThemeMachineSnapshot.CurrentStateCode,
                    ToStateCode = v2State.ThemeMachineSnapshot.CurrentStateCode,
                    TransitionId = null,
                    ReasonCode = "ReturnBeatCompletionAutoDetected",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        signal = returnBeatAutoDetectionResult.MatchedSignal,
                        sourceInteractionId = returnBeatAutoDetectionResult.SourceInteractionId,
                        state = v2State.ThemeMachineSnapshot.CurrentStateCode,
                        definitionId = v2State.ThemeMachineSnapshot.DefinitionId
                    }),
                    OccurredUtc = DateTime.UtcNow
                }
            ],
            cancellationToken);

            _logger.LogInformation(
                "RolePlayV2 return-beat completion auto-detected: SessionId={SessionId} ScenarioId={ScenarioId} State={State} InteractionId={InteractionId}",
                session.Id,
                v2State.ActiveScenarioId,
                v2State.ThemeMachineSnapshot.CurrentStateCode,
                returnBeatAutoDetectionResult.SourceInteractionId ?? string.Empty);
        }

        var returnBeatCompletionRequested = IsReturnBeatCompletionRequested(session);
        if (returnBeatCompletionRequested)
        {
            var requestedAtState = v2State.ThemeMachineSnapshot?.CurrentStateCode;
            var returnBeatApplied = TryApplyExplicitReturnBeatCompletion(v2State);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "ReturnBeatCommandProcessed",
                Severity = returnBeatApplied ? "Info" : "Warning",
                Summary = returnBeatApplied
                    ? "Return-beat completion command applied"
                    : "Return-beat completion command ignored",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    command = "/returnbeat",
                    applied = returnBeatApplied,
                    requestedAtState,
                    currentState = v2State.ThemeMachineSnapshot?.CurrentStateCode,
                    alreadyCompleted = v2State.ThemeMachineSnapshot?.ReturnBeatCompleted ?? false
                })
            }, cancellationToken);

            if (returnBeatApplied
                && v2State.ThemeMachineSnapshot is not null
                && !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
            {
                await _stateRepository.SaveThemeMachineDiagnosticEventsAsync(
                [
                    new ThemeMachineDiagnosticEvent
                    {
                        SessionId = session.Id,
                        ThemeId = v2State.ActiveScenarioId,
                        MachineKey = v2State.ThemeMachineSnapshot.MachineKey,
                        DefinitionVersion = v2State.ThemeMachineSnapshot.DefinitionVersion,
                        EventType = "signal",
                        FromStateCode = v2State.ThemeMachineSnapshot.CurrentStateCode,
                        ToStateCode = v2State.ThemeMachineSnapshot.CurrentStateCode,
                        TransitionId = null,
                        ReasonCode = "ReturnBeatCompletionRecorded",
                        PayloadJson = JsonSerializer.Serialize(new
                        {
                            command = "/returnbeat",
                            state = v2State.ThemeMachineSnapshot.CurrentStateCode,
                            definitionId = v2State.ThemeMachineSnapshot.DefinitionId
                        }),
                        OccurredUtc = DateTime.UtcNow
                    }
                ],
                cancellationToken);

                _logger.LogInformation(
                    "RolePlayV2 return-beat completion recorded: SessionId={SessionId} ScenarioId={ScenarioId} State={State}",
                    session.Id,
                    v2State.ActiveScenarioId,
                    v2State.ThemeMachineSnapshot.CurrentStateCode);
            }
            else
            {
                _logger.LogWarning(
                    "RolePlayV2 return-beat command ignored: SessionId={SessionId} ScenarioId={ScenarioId} State={State}",
                    session.Id,
                    v2State.ActiveScenarioId ?? string.Empty,
                    requestedAtState ?? "(none)");
            }
        }

        var preSelectionDirective = BuildDirectiveFromSnapshot(session.Id, v2State.ThemeMachineSnapshot);

        var candidates = await BuildScenarioCandidatesAsync(session, v2State, cancellationToken);
        var (linkedNarrativeGateProfileId, linkedNarrativeGateRules) = await ResolveThemeNarrativeGateConfigAsync(session, v2State, cancellationToken);
        v2State.SelectedNarrativeGateProfileId = linkedNarrativeGateProfileId;
        var blockedScenarioIds = ResolveBlockedScenarioIdsFromDirective(session.Id, v2State, preSelectionDirective, candidates);

        var manualOverrideLockActive = IsManualThemeOverrideLockActive(session);

        var evaluations = manualOverrideLockActive
            ? Array.Empty<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>()
            : await _scenarioSelectionService.EvaluateCandidatesAsync(v2State, candidates, cancellationToken, blockedScenarioIds);
        await _stateRepository.SaveCandidateEvaluationsAsync(evaluations, cancellationToken);

        var inResetPhase = v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset;
        var commitResult = (manualOverrideLockActive || inResetPhase)
            ? new ScenarioCommitResult
            {
                Committed = false,
                ScenarioId = v2State.ActiveScenarioId,
                UpdatedConsecutiveLeadCount = v2State.ConsecutiveLeadCount,
                Reason = manualOverrideLockActive ? "ManualOverrideLockActive" : "ResetPhase"
            }
            : await _scenarioSelectionService.TryCommitScenarioAsync(v2State, evaluations, cancellationToken);

        if (v2State.CurrentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
        {
            var gateSnapshot = ParseBuildUpGateAudit(commitResult.AuditMetadataJson);
            var gateSummary = gateSnapshot.Passed switch
            {
                true => "passed",
                false => "blocked",
                null => "not-configured"
            };

            _logger.LogInformation(
                "RolePlayV2 BuildUp commit gate {GateSummary}: SessionId={SessionId} ProfileId={ProfileId} ProfileName={ProfileName} Configured={Configured} Committed={Committed} CandidateScenarioId={CandidateScenarioId} InteractionCount={InteractionCount} CandidateCount={CandidateCount} Reason={Reason}",
                gateSummary,
                session.Id,
                gateSnapshot.ProfileId,
                gateSnapshot.ProfileName,
                gateSnapshot.Configured,
                commitResult.Committed,
                commitResult.ScenarioId,
                v2State.InteractionCountInPhase,
                evaluations.Count,
                commitResult.Reason);

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "AdaptiveCommitGateEvaluated",
                Severity = gateSnapshot.Passed == false ? "Warning" : "Info",
                Summary = gateSnapshot.Passed == false
                    ? "BuildUp commit blocked by gate rules"
                    : gateSnapshot.Passed == true
                        ? "BuildUp commit gate passed"
                        : "BuildUp commit gate not configured",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    phase = v2State.CurrentPhase.ToString(),
                    interactionCount = v2State.InteractionCountInPhase,
                    candidateCount = evaluations.Count,
                    selectedScenarioId = commitResult.ScenarioId,
                    committed = commitResult.Committed,
                    reason = commitResult.Reason,
                    gateAudit = commitResult.AuditMetadataJson
                })
            }, cancellationToken);
        }

        v2State.ConsecutiveLeadCount = commitResult.UpdatedConsecutiveLeadCount;
        var commitApplied = false;
        if (commitResult.Committed && !string.IsNullOrWhiteSpace(commitResult.ScenarioId))
        {
            var currentPhase = v2State.CurrentPhase;
            var sameScenarioAlreadyActive = string.Equals(
                v2State.ActiveScenarioId,
                commitResult.ScenarioId,
                StringComparison.OrdinalIgnoreCase);
            var enteringArc = currentPhase is DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
                or DreamGenClone.Domain.RolePlay.NarrativePhase.Reset;
            var hasActiveScenario = !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId);
            // Active-scenario lock: once an ActiveScenarioId is set, it must not be replaced by a
            // different scenario from a later evaluation cycle. The only legitimate way to clear the
            // active scenario is the explicit completion path (ScenarioLifecycleService), which sets
            // ActiveScenarioId back to null. This prevents the engine from switching themes after a
            // scenario has been selected, even when re-entering BuildUp/Reset.
            var suppressActiveScenarioSwitch = hasActiveScenario && !sameScenarioAlreadyActive;
            // First-scenario-selection guard: when we are in BuildUp and the arc has never had an
            // ActiveScenarioId yet, the BuildUp commit gate was evaluated against an
            // InteractionCountInPhase that accumulated during the Observing window (no scenario
            // assigned). That makes any configured InteractionsSinceCommitment threshold trivially
            // pass on the very first turn a candidate becomes eligible, which collapses the BuildUp
            // phase to zero turns. Treat this evaluation as a scenario SELECTION (assign and reset
            // the per-phase counter) and keep the session in BuildUp; the next turn will re-evaluate
            // the configured per-theme narrative gate against a counter that reflects only turns the
            // selected scenario has actually been in play.
            var firstScenarioSelectionInBuildUp = currentPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp
                && !hasActiveScenario;

            if (suppressActiveScenarioSwitch)
            {
                _logger.LogInformation(
                    "RolePlayV2 active-scenario switch suppressed (active-scenario lock): SessionId={SessionId} Phase={Phase} ActiveScenarioId={ActiveScenarioId} CandidateScenarioId={CandidateScenarioId}",
                    session.Id,
                    currentPhase,
                    v2State.ActiveScenarioId,
                    commitResult.ScenarioId);
            }
            else if (firstScenarioSelectionInBuildUp)
            {
                // Observation window guard: do not select a scenario while the theme tracker is
                // still in its configured observation window (ObservedTurnCount <= SelectionMinimumTurns).
                // The backfill path below handles first-scenario assignment once the observation
                // window expires. Without this guard, a low InteractionsSinceCommitment threshold on
                // the configured gate profile would allow the commit gate to fire before the minimum
                // observation turns have elapsed � bypassing the SelectionMinimumTurns setting.
                var fsInObservationWindow = session.AdaptiveState.SelectionMinimumTurns > 0
                    && session.AdaptiveState.ObservedTurnCount <= session.AdaptiveState.SelectionMinimumTurns;

                if (fsInObservationWindow)
                {
                    _logger.LogInformation(
                        "RolePlayV2 first scenario selection deferred (observation window active): SessionId={SessionId} ScenarioId={ScenarioId} ObservedTurnCount={ObservedTurns} SelectionMinimumTurns={MinTurns}",
                        session.Id,
                        commitResult.ScenarioId,
                        session.AdaptiveState.ObservedTurnCount,
                        session.AdaptiveState.SelectionMinimumTurns);
                }
                else
                {
                    v2State.ActiveScenarioId = commitResult.ScenarioId;
                    // Do NOT reset InteractionCountInPhase here. The interactions that accumulated
                    // before the scenario was selected were scored against this scenario's candidacy;
                    // resetting to 0 forces the gate to re-count from scratch, causing the session
                    // to remain in BuildUp for another full threshold window despite the gate having
                    // already passed. The counter continues from its current value so the next gate
                    // evaluation correctly sees the legitimate interaction count.
                    // Phase remains BuildUp. Do not set commitApplied=true: the configured BuildUp
                    // narrative gate must still be evaluated against post-selection turn counts.
                    _logger.LogInformation(
                        "RolePlayV2 first scenario selected during BuildUp; phase commit deferred until configured BuildUp narrative gate is met against post-selection turns. SessionId={SessionId} ScenarioId={ScenarioId} CurrentInteractionCountInPhase={CurrentInteractionCountInPhase}",
                        session.Id,
                        commitResult.ScenarioId,
                        v2State.InteractionCountInPhase);
                    await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                    {
                        SessionId = session.Id,
                        EventKind = "AdaptiveScenarioFirstSelected",
                        Severity = "Info",
                        Summary = "First scenario selected in BuildUp; commit deferred to satisfy configured narrative gate.",
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            scenarioId = commitResult.ScenarioId,
                            currentInteractionCountInPhase = v2State.InteractionCountInPhase,
                            reason = commitResult.Reason
                        })
                    }, cancellationToken);
                }
            }
            else
            {
                if (!sameScenarioAlreadyActive)
                {
                    ClearPhaseOverrideLock(v2State);
                    await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                    {
                        SessionId = session.Id,
                        EventKind = "PhaseOverrideLockCleared",
                        Severity = "Info",
                        Summary = "Phase override lock cleared due scenario switch",
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            reason = "ScenarioSwitch",
                            scenarioId = commitResult.ScenarioId
                        })
                    }, cancellationToken);
                }

                v2State.ActiveScenarioId = commitResult.ScenarioId;
                // Only advance/reset to Committed when entering a fresh arc from BuildUp or Reset.
                // Do NOT revert an already-advanced phase (Approaching, Climax) back to Committed �
                // that would undo any /nextphase advances the user has already made.
                if (enteringArc)
                {
                    v2State.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed;

                    // Reset phase interaction cadence only when a new arc is entered.
                    v2State.InteractionCountInPhase = 0;

                    // Write the BuildUp?Committed transition event and encounter summary records.
                    // This transition happens via the commit gate (not the lifecycle service), so we
                    // must create the event here � no TransitionEvent flows through the lifecycle path.
                    var commitTransitionEvent = new DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent
                    {
                        TransitionId = Guid.NewGuid().ToString("N"),
                        SessionId    = session.Id,
                        FromPhase    = currentPhase,
                        ToPhase      = DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
                        TriggerType  = DreamGenClone.Domain.RolePlay.TransitionTriggerType.Threshold,
                        ReasonCode   = "BUILDUP_TO_COMMITTED",
                        EvidencePayload = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            scenarioId = commitResult.ScenarioId,
                            reason     = commitResult.Reason
                        }),
                        OccurredUtc = DateTime.UtcNow
                    };
                    await _stateRepository.SaveTransitionEventAsync(commitTransitionEvent, cancellationToken);

                    if (_encounterSummaryService is not null)
                    {
                        IReadOnlySet<string>? commitAllowedIds = null;
                        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
                        {
                            var commitScenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                            if (commitScenario is not null)
                            {
                                var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                foreach (var c in commitScenario.Characters)
                                    if (!string.IsNullOrWhiteSpace(c.Name)) allowed.Add(c.Name);
                                if (!string.IsNullOrWhiteSpace(session.PersonaName))
                                    allowed.Add(session.PersonaName);
                                commitAllowedIds = allowed;
                            }
                        }

                        var commitSummaries = await _encounterSummaryService.GenerateTemplatesAsync(commitTransitionEvent, v2State, commitAllowedIds, cancellationToken);
                        foreach (var summary in commitSummaries)
                        {
                            await _encounterSummaryService.SaveAsync(summary, cancellationToken);
                            v2State.EncounterSummaries.Add(summary);
                        }

                        if (_backgroundJobQueue is not null && _memoryOptions?.Value.EnableLlmSummaryEnhancement == true)
                        {
                            foreach (var summary in commitSummaries)
                            {
                                _backgroundJobQueue.Enqueue(
                                    BackgroundJobTypes.EncounterSummaryEnhancement,
                                    System.Text.Json.JsonSerializer.Serialize(new EncounterSummaryJobPayload
                                    {
                                        SessionId   = session.Id,
                                        CycleIndex  = v2State.CycleIndex,
                                        SummaryId   = summary.Id,
                                        SummaryType = summary.SummaryType.ToString()
                                    }),
                                    dedupeKey: $"enc-summary:{session.Id}:{summary.Id}");
                            }
                        }
                    }
                }
            }
        }

        // BuildUp always needs a selected scenario/theme even before commit gates allow phase promotion.
        // However, while the theme tracker is still in its observation period (configured per profile via
        // ThemeSelectionTurnsPerTheme), do not backfill � we have not observed enough turns yet to pick.
        if (v2State.CurrentPhase == NarrativePhase.BuildUp
            && string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
        {
            var observing = session.AdaptiveState.SelectionMinimumTurns > 0
                && session.AdaptiveState.ObservedTurnCount <= session.AdaptiveState.SelectionMinimumTurns;

            if (observing)
            {
                _logger.LogInformation(
                    "RolePlayV2 BuildUp active scenario backfill skipped (observing): SessionId={SessionId} ObservedTurnCount={ObservedTurns} SelectionMinimumTurns={MinTurns}",
                    session.Id,
                    session.AdaptiveState.ObservedTurnCount,
                    session.AdaptiveState.SelectionMinimumTurns);
            }
            else
            {
                var inferredScenarioId = !string.IsNullOrWhiteSpace(commitResult.ScenarioId)
                    ? commitResult.ScenarioId
                    : evaluations.FirstOrDefault(x => x.StageBEligible)?.ScenarioId
                        ?? evaluations.FirstOrDefault()?.ScenarioId
                        ?? candidates.FirstOrDefault()?.ScenarioId;

                if (!string.IsNullOrWhiteSpace(inferredScenarioId))
                {
                    v2State.ActiveScenarioId = inferredScenarioId;
                    // Reset BuildUp's phase-local interaction cadence to the moment a scenario is
                    // actually selected. Without this, turns accumulated while Observing (when no
                    // scenario was committed yet) count toward BuildUp's lifecycle gate and the
                    // phase is immediately promoted past as soon as the backfill happens.
                    v2State.InteractionCountInPhase = 0;
                    _logger.LogInformation(
                        "RolePlayV2 BuildUp active scenario backfilled: SessionId={SessionId} ScenarioId={ScenarioId} Reason={Reason}",
                        session.Id,
                        inferredScenarioId,
                        commitResult.Reason);
                }
            }
        }

        try
        {
            await EnsureThemeMachineResolutionGuardAsync(session, v2State, cancellationToken);
            _ = await EvaluateThemeMachineAsync(session, v2State, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await PersistThemeMachineFailureDiagnosticAsync(
                session,
                v2State,
                "ThemeMachineEvaluationFailure",
                ex.Message,
                cancellationToken);
            _logger.LogError(
                ex,
                "RolePlayV2 machine evaluation failed: SessionId={SessionId} ScenarioId={ScenarioId}",
                session.Id,
                v2State.ActiveScenarioId);
            throw;
        }

        var activeScenarioEvaluation = evaluations.FirstOrDefault(x =>
            !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId)
            && string.Equals(x.ScenarioId, v2State.ActiveScenarioId, StringComparison.OrdinalIgnoreCase));

        var lifecycleConfidence = commitApplied
            ? (commitResult.SelectedEvaluation?.Confidence ?? activeScenarioEvaluation?.Confidence ?? 0m)
            : (activeScenarioEvaluation?.Confidence ?? 0m);
        var lifecycleFitScore = commitApplied
            ? (commitResult.SelectedEvaluation?.FitScore ?? activeScenarioEvaluation?.FitScore ?? 0m)
            : (activeScenarioEvaluation?.FitScore ?? 0m);

        var lifecycle = await _scenarioLifecycleService.EvaluateTransitionAsync(
            v2State,
            new LifecycleInputs
            {
                InteractionsSinceCommitment = v2State.InteractionCountInPhase,
                ClimaxCompletionRequested = climaxCompletionRequested,
                ManualAdvanceTargetPhase = manualPhaseAdvanceTarget,
                NarrativeGateProfileId = linkedNarrativeGateProfileId,
                NarrativeGateRules = linkedNarrativeGateRules,
                SkipDefaultNarrativeGateProfileFallback = linkedNarrativeGateRules.Count > 0 || string.IsNullOrWhiteSpace(linkedNarrativeGateProfileId),
                ActiveScenarioConfidence = lifecycleConfidence,
                ActiveScenarioFitScore = lifecycleFitScore,
                EvidenceSummary = commitResult.Reason
            },
            cancellationToken);

        if (lifecycle.Transitioned)
        {
            var transitionSourcePhase = v2State.CurrentPhase;
            v2State.CurrentPhase = lifecycle.TargetPhase;
            v2State.InteractionCountInPhase = 0;

            if (_suppressNarrativeAfterPhaseChange)
            {
                session.SuppressNextNarrativeAfterDecision = true;
            }

            if (manualPhaseAdvanceTarget.HasValue
                && lifecycle.TargetPhase == manualPhaseAdvanceTarget.Value
                && IsForwardPhaseTransition(transitionSourcePhase, lifecycle.TargetPhase)
                && !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
            {
                v2State.PhaseOverrideFloor = lifecycle.TargetPhase;
                v2State.PhaseOverrideScenarioId = v2State.ActiveScenarioId;
                v2State.PhaseOverrideCycleIndex = v2State.CycleIndex;
                v2State.PhaseOverrideSource = "/nextphase";
                v2State.PhaseOverrideAppliedUtc = DateTime.UtcNow;
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "PhaseOverrideLockApplied",
                    Severity = "Info",
                    Summary = "Phase override lock applied via /nextphase",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        fromPhase = transitionSourcePhase.ToString(),
                        toPhase = lifecycle.TargetPhase.ToString(),
                        floorPhase = v2State.PhaseOverrideFloor?.ToString(),
                        scenarioId = v2State.PhaseOverrideScenarioId,
                        cycleIndex = v2State.PhaseOverrideCycleIndex,
                        source = v2State.PhaseOverrideSource
                    })
                }, cancellationToken);
            }

            if (lifecycle.TransitionEvent is not null)
            {
                await _stateRepository.SaveTransitionEventAsync(lifecycle.TransitionEvent, cancellationToken);

                if (_encounterSummaryService is not null)
                {
                    // Build an allowlist of characters that belong to the scenario and persona.
                    // CharacterSnapshots may contain names extracted from narrative text that are
                    // not actual scenario participants � filter those out.
                    IReadOnlySet<string>? allowedCharacterIds = null;
                    if (!string.IsNullOrWhiteSpace(session.ScenarioId))
                    {
                        var scenarioForSummary = await _scenarioService.GetScenarioAsync(session.ScenarioId);
                        if (scenarioForSummary is not null)
                        {
                            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            foreach (var c in scenarioForSummary.Characters)
                            {
                                if (!string.IsNullOrWhiteSpace(c.Name))
                                    allowed.Add(c.Name);
                            }
                            if (!string.IsNullOrWhiteSpace(session.PersonaName))
                                allowed.Add(session.PersonaName);
                            allowedCharacterIds = allowed;
                        }
                    }

                    var summaries = await _encounterSummaryService.GenerateTemplatesAsync(lifecycle.TransitionEvent, v2State, allowedCharacterIds, cancellationToken);
                    foreach (var summary in summaries)
                    {
                        await _encounterSummaryService.SaveAsync(summary, cancellationToken);
                        v2State.EncounterSummaries.Add(summary);
                    }
                    if (summaries.Count > 0)
                    {
                        _logger.LogInformation(
                            "Encounter summaries written: {Count} records for session {SessionId} cycle {CycleIndex} transition {FromPhase}?{ToPhase}",
                            summaries.Count, session.Id, v2State.CycleIndex,
                            lifecycle.TransitionEvent.FromPhase, lifecycle.TransitionEvent.ToPhase);
                    }

                    if (_backgroundJobQueue is not null
                        && _memoryOptions?.Value.EnableLlmSummaryEnhancement == true)
                    {
                        foreach (var summary in summaries)
                        {
                            _backgroundJobQueue.Enqueue(
                                BackgroundJobTypes.EncounterSummaryEnhancement,
                                JsonSerializer.Serialize(new EncounterSummaryJobPayload
                                {
                                    SessionId   = session.Id,
                                    CycleIndex  = v2State.CycleIndex,
                                    SummaryId   = summary.Id,
                                    SummaryType = summary.SummaryType.ToString()
                                }),
                                dedupeKey: $"enc-summary:{session.Id}:{summary.Id}");
                        }
                        _logger.LogInformation(
                            "Enqueued {Count} encounter summary enhancement job(s) for session {SessionId} cycle {CycleIndex} transition {FromPhase}?{ToPhase}",
                            summaries.Count, session.Id, v2State.CycleIndex,
                            lifecycle.TransitionEvent.FromPhase, lifecycle.TransitionEvent.ToPhase);
                    }
                }
            }

            if (lifecycle.TargetPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset)
            {
                ClearPhaseOverrideLock(v2State);
                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "ClimaxToResetTransition",
                    Severity = "Info",
                    Summary = "Climax ? Reset phase transition. Active scenario and theme preserved.",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        fromPhase = transitionSourcePhase.ToString(),
                        toPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.Reset.ToString(),
                        activeScenarioId = v2State.ActiveScenarioId,
                        cycleIndex = v2State.CycleIndex,
                        reason = lifecycle.Reason
                    })
                }, cancellationToken);
            }

            if (transitionSourcePhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Reset
                && lifecycle.TargetPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp)
            {
                // Reset ? BuildUp: apply stat decay, theme score penalties, and successor bonuses.
                var completedScenarioId = v2State.ActiveScenarioId;

                var statsBefore = v2State.CharacterSnapshots
                    .Select(s => new { s.CharacterId, s.Desire, s.Restraint, Tension = s.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50, Connection = s.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50 })
                    .ToList();

                v2State = await _scenarioLifecycleService.ExecuteResetAsync(
                    v2State,
                    ResetReason.Completion,
                    session.AdaptiveState.CharacterStats.Values
                        .Where(b => !string.IsNullOrWhiteSpace(b.CharacterId) && b.BaselineStats.Count > 0)
                        .ToDictionary(
                            b => b.CharacterId,
                            b => (IReadOnlyDictionary<string, int>)b.BaselineStats,
                            StringComparer.OrdinalIgnoreCase),
                    await ResolveThemeStatDecayScaleOverridesAsync(completedScenarioId, cancellationToken),
                    cancellationToken);
                // ExecuteResetAsync sets CurrentPhase = Reset and increments CycleIndex � restore BuildUp.
                v2State.CurrentPhase = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp;

                var statsAfter = v2State.CharacterSnapshots
                    .Select(s => new { s.CharacterId, s.Desire, s.Restraint, Tension = s.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50, Connection = s.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50 })
                    .ToList();

                var themeScoresBefore = session.AdaptiveState.ThemeScores
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Score);

                await ApplyThemeSemiResetAsync(session.AdaptiveState, completedScenarioId, cancellationToken);

                // ExecuteResetAsync returned a new AdaptiveScenarioState instance � v2State is now a
                // different object than session.AdaptiveState. Copy the semi-reset theme state (penalty/
                // bonus scores, cooldowns, observer mode setup) and character profile bindings from the
                // old session.AdaptiveState into v2State so they survive the cycle boundary and are
                // correctly persisted.
                v2State.ThemeScores = session.AdaptiveState.ThemeScores;
                v2State.PrimaryThemeId = session.AdaptiveState.PrimaryThemeId;
                v2State.SecondaryThemeId = session.AdaptiveState.SecondaryThemeId;
                v2State.ThemeSelectionRule = session.AdaptiveState.ThemeSelectionRule;
                v2State.ObservedTurnCount = session.AdaptiveState.ObservedTurnCount;
                v2State.SelectionMinimumTurns = session.AdaptiveState.SelectionMinimumTurns;
                v2State.RecentEvidence = session.AdaptiveState.RecentEvidence;
                // Preserve character profile bindings so the Adaptive panel continues to show
                // the correct "BASELINE CHARACTER" label after cycle reset.
                v2State.CharacterEncounterProfileIds = session.AdaptiveState.CharacterEncounterProfileIds;
                v2State.CharacterRoles = session.AdaptiveState.CharacterRoles;

                var themeScoresAfter = session.AdaptiveState.ThemeScores
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Score);

                await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                {
                    SessionId = session.Id,
                    EventKind = "ResetToObserverTransition",
                    Severity = "Info",
                    Summary = $"Reset ? Observer (BuildUp). Stat decay applied. Theme penalties/bonuses applied. ActiveScenarioId cleared. CompletedScenario={completedScenarioId ?? "(none)"}",
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        completedScenarioId,
                        newCycleIndex = v2State.CycleIndex,
                        statDecay = statsBefore.Select(before =>
                        {
                            var after = statsAfter.FirstOrDefault(a => a.CharacterId == before.CharacterId);
                            return new
                            {
                                characterId = before.CharacterId,
                                desire = new { before = before.Desire, after = after?.Desire },
                                restraint = new { before = before.Restraint, after = after?.Restraint },
                                tension = new { before = before.Tension, after = after?.Tension },
                                connection = new { before = before.Connection, after = after?.Connection }
                            };
                        }),
                        themeScoreChanges = themeScoresBefore
                            .Where(kvp => Math.Abs(kvp.Value - (themeScoresAfter.TryGetValue(kvp.Key, out var a) ? a : kvp.Value)) > 0.001)
                            .Select(kvp => new
                            {
                                themeId = kvp.Key,
                                before = kvp.Value,
                                after = themeScoresAfter.TryGetValue(kvp.Key, out var af) ? af : kvp.Value
                            }),
                        activeScenarioIdCleared = completedScenarioId
                    })
                }, cancellationToken);
            }
        }

        if (TryGetActivePhaseOverrideFloor(v2State, out var phaseFloor)
            && IsForwardPhaseTransition(v2State.CurrentPhase, phaseFloor))
        {
            v2State.CurrentPhase = phaseFloor;
        }

        var significantStatChange = HasSignificantStatChange(previousV2State, v2State);
        var conceptCandidates = BuildConceptCandidates(session);
        var conceptTriggers = _promptComposer.ResolveConceptInjectionTriggers(trigger, lifecycle.Transitioned, significantStatChange);
        foreach (var conceptTrigger in conceptTriggers)
        {
            var conceptContext = _promptComposer.BuildConceptContext(conceptCandidates, conceptTrigger);
            var conceptResult = await _conceptInjectionService.BuildGuidanceAsync(v2State, conceptContext, cancellationToken);
            await _stateRepository.SaveConceptInjectionAsync(session.Id, conceptResult, cancellationToken);
        }

        var effectiveDecisionTrigger = lifecycle.Transitioned && _enablePhaseChangeDecisionPrompts
            ? DecisionTrigger.PhaseChanged
            : trigger;

        var directQuestionSignal = TryDetectDirectQuestionSignal(session, v2State);
        var sceneLocationSignal = _enableLocationServices
            ? await DetectSceneLocationSignalAsync(session, v2State, cancellationToken)
            : SceneLocationSignal.None;

        if (sceneLocationSignal.Changed)
        {
            if (_enableSceneLocationDecisionPrompts)
            {
                effectiveDecisionTrigger = DecisionTrigger.SceneLocationChanged;
            }

            _logger.LogInformation(
                RolePlayV2LogEvents.SceneLocationChangedDetected,
                session.Id,
                sceneLocationSignal.PreviousLocation ?? string.Empty,
                sceneLocationSignal.CurrentLocation ?? string.Empty);
        }

        if (directQuestionSignal.IsDetected)
        {
            effectiveDecisionTrigger = DecisionTrigger.CharacterDirectQuestion;
            _logger.LogInformation(
                RolePlayV2LogEvents.DirectQuestionDetected,
                session.Id,
                directQuestionSignal.AskingActorId ?? string.Empty,
                directQuestionSignal.TargetActorId ?? string.Empty);
        }

        var hasPendingDecision = await HasPendingDecisionPointAsync(session, cancellationToken);
        var isInDecisionCooldown = hasPendingDecision
            ? false
            : await HasRecentDecisionPointForContextAsync(session, v2State, effectiveDecisionTrigger, cancellationToken);
        var decisionSkipReasons = new List<string>();
        var evaluatedContextCount = 0;
        var createdDecisionCount = 0;
        var triggerEligibleForDecisionCreation = IsDecisionTriggerEligible(effectiveDecisionTrigger, v2State);
        var bypassActiveScenarioRequirement = effectiveDecisionTrigger is DecisionTrigger.CharacterDirectQuestion or DecisionTrigger.SceneLocationChanged;
        var hasActiveScenarioForDecisionCreation = !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId) || bypassActiveScenarioRequirement;

        if (hasPendingDecision)
        {
            decisionSkipReasons.Add("PendingDecisionExists");
        }

        if (!hasPendingDecision && isInDecisionCooldown)
        {
            decisionSkipReasons.Add("ContextCooldownActive");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && !triggerEligibleForDecisionCreation)
        {
            decisionSkipReasons.Add("TriggerCadenceNotReached");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && triggerEligibleForDecisionCreation && !hasActiveScenarioForDecisionCreation)
        {
            decisionSkipReasons.Add("NoActiveScenario");
        }

        if (!hasPendingDecision && !isInDecisionCooldown && triggerEligibleForDecisionCreation && hasActiveScenarioForDecisionCreation)
        {
            if (!_enableDecisionPrompts)
            {
                decisionSkipReasons.Add("DecisionPromptsDisabled");
            }
            else
            {
            var decisionContexts = BuildDecisionGenerationContexts(
                session,
                v2State,
                effectiveDecisionTrigger,
                directQuestionSignal,
                sceneLocationSignal.CurrentLocation);
            evaluatedContextCount = decisionContexts.Count;
            foreach (var decisionContext in decisionContexts)
            {
                var decisionPoint = await _decisionPointService.TryCreateDecisionPointAsync(
                    v2State,
                    effectiveDecisionTrigger,
                    decisionContext,
                    cancellationToken);
                if (decisionPoint is null)
                {
                    continue;
                }

                createdDecisionCount++;

                var options = decisionPoint.OptionIds
                    .Select(optionId => BuildDecisionOptionForContext(session, decisionPoint, optionId))
                    .ToList();
                var rewriteResult = await TryApplyAiDecisionOptionAnswersAsync(session, decisionPoint, options, cancellationToken);
                options = rewriteResult.Options;

                await _stateRepository.SaveDecisionPointAsync(decisionPoint, options, cancellationToken);

                if (_debugEventSink is not null)
                {
                    await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
                    {
                        SessionId = session.Id,
                        EventKind = "DecisionPromptCreated",
                        Severity = "Info",
                        ActorName = ResolveLocationActorLabel(session, decisionPoint.TargetActorId),
                        Summary = $"Decision prompt created ({decisionPoint.TriggerSource}) for {ResolveLocationActorLabel(session, decisionPoint.TargetActorId)}.",
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            decisionPointId = decisionPoint.DecisionPointId,
                            trigger = decisionPoint.TriggerSource,
                            phase = decisionPoint.Phase.ToString(),
                            askingActor = ResolveLocationActorLabel(session, decisionPoint.AskingActorName),
                            targetActor = ResolveLocationActorLabel(session, decisionPoint.TargetActorId),
                            contextSummary = decisionPoint.ContextSummary,
                            rewriteApplied = rewriteResult.UsedAiRewrite,
                            rewriteStatus = rewriteResult.Status,
                            rewriteReason = rewriteResult.Reason,
                            options = options.Select(x => new
                            {
                                optionId = x.OptionId,
                                displayText = x.DisplayText,
                                responsePreview = x.ResponsePreview,
                                visibilityMode = x.VisibilityMode.ToString(),
                                statDeltaMap = x.StatDeltaMap
                            })
                        })
                    }, cancellationToken);
                }

                _logger.LogInformation(
                    RolePlayV2LogEvents.DecisionPointCreated,
                    session.Id,
                    decisionPoint.DecisionPointId,
                    effectiveDecisionTrigger,
                    decisionPoint.AskingActorName ?? string.Empty,
                    decisionPoint.TargetActorId ?? string.Empty);

            }

            if (createdDecisionCount == 0)
            {
                decisionSkipReasons.Add("NoEligibleDecisionGenerated");
            }
            } // end else (_enableDecisionPrompts)
        }

        _logger.LogInformation(
            RolePlayV2LogEvents.DecisionAttemptEvaluated,
            session.Id,
            effectiveDecisionTrigger,
            hasPendingDecision,
            isInDecisionCooldown,
            evaluatedContextCount,
            createdDecisionCount,
            decisionSkipReasons.Count == 0 ? "None" : string.Join(",", decisionSkipReasons));

        if (_debugEventSink is not null)
        {
            var debugActor = ResolveLocationActorLabel(session, directQuestionSignal.AskingActorId);
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "LocationStateUpdated",
                Severity = "Info",
                ActorName = string.IsNullOrWhiteSpace(debugActor) ? directQuestionSignal.AskingActorId : debugActor,
                Summary = $"Location state refreshed ({v2State.CharacterLocations.Count} truth rows, {v2State.CharacterLocationPerceptions.Count} perception rows)",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    trigger = effectiveDecisionTrigger.ToString(),
                    sceneLocationChanged = sceneLocationSignal.Changed,
                    previousSceneLocation = sceneLocationSignal.PreviousLocation,
                    currentSceneLocation = v2State.CurrentSceneLocation,
                    characterLocations = v2State.CharacterLocations
                        .OrderBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new
                        {
                            characterId = x.CharacterId,
                            characterLabel = ResolveLocationActorLabel(session, x.CharacterId),
                            characterType = ResolveLocationActorType(session, x.CharacterId),
                            trueLocation = x.TrueLocation,
                            isHidden = x.IsHidden,
                            updatedUtc = x.UpdatedUtc
                        }),
                    characterLocationPerceptions = v2State.CharacterLocationPerceptions
                        .OrderBy(x => x.ObserverCharacterId, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(x => x.TargetCharacterId, StringComparer.OrdinalIgnoreCase)
                        .Select(x => new
                        {
                            observerCharacterId = x.ObserverCharacterId,
                            observerLabel = ResolveLocationActorLabel(session, x.ObserverCharacterId),
                            observerType = ResolveLocationActorType(session, x.ObserverCharacterId),
                            targetCharacterId = x.TargetCharacterId,
                            targetLabel = ResolveLocationActorLabel(session, x.TargetCharacterId),
                            targetType = ResolveLocationActorType(session, x.TargetCharacterId),
                            perceivedLocation = x.PerceivedLocation,
                            confidence = x.Confidence,
                            hasLineOfSight = x.HasLineOfSight,
                            isInProximity = x.IsInProximity,
                            knowledgeSource = x.KnowledgeSource,
                            updatedUtc = x.UpdatedUtc
                        })
                })
            }, cancellationToken);
        }

        await _stateRepository.SaveFormulaVersionReferenceAsync(
            session.Id,
            new DreamGenClone.Domain.RolePlay.FormulaConfigVersion
            {
                FormulaVersionId = "rpv2-default",
                Name = "RolePlay V2 Default",
                ParameterPayload = "{\"nearTieThreshold\":0.8,\"requiredLeadCount\":2}",
                EffectiveFromUtc = DateTime.UtcNow,
                IsDefault = true
            },
            v2State.CycleIndex,
            cancellationToken);

        // Beat cursor: init when entering Climax, reset when leaving, advance when staying.
        // The cursor is only active for themes tagged [BeatStyle:episodic] in their Climax
        // phase guidance. Themes without that tag use an unstructured climax and the beat
        // catalog should not be applied.
        var priorPhase = previousV2State?.CurrentPhase;
        var finalPhase = v2State.CurrentPhase;

        RPTheme? beatCursorTheme = null;
        if (_rpThemeService is not null && !string.IsNullOrWhiteSpace(v2State.ActiveScenarioId))
        {
            try { beatCursorTheme = await _rpThemeService.GetThemeAsync(v2State.ActiveScenarioId, cancellationToken); }
            catch (Exception ex) { _logger.LogDebug(ex, "ClimaxBeatCursor: could not load theme {ThemeId}: SessionId={SessionId}", v2State.ActiveScenarioId, session.Id); }
        }
        var isEpisodicBeatStyle = RolePlayAssistantPrompts.IsEpisodicBeatStyle(beatCursorTheme, "Climax");

        if (finalPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
                 && v2State.CurrentBeatCode != null)
        {
            // Left Climax: clear cursor.
            v2State.CurrentBeatCode = null;
            v2State.TurnsInCurrentBeat = 0;
        }
        else if (finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
                 && !isEpisodicBeatStyle
                 && v2State.CurrentBeatCode != null)
        {
            // In Climax but the active theme does not have [BeatStyle:episodic]: clear any
            // existing cursor so it is not injected into prompts or reported in diagnostics.
            v2State.CurrentBeatCode = null;
            v2State.TurnsInCurrentBeat = 0;
            _logger.LogInformation(
                "ClimaxBeatCursor cleared: theme {ThemeId} does not have [BeatStyle:episodic]: SessionId={SessionId}",
                v2State.ActiveScenarioId, session.Id);
        }
        else if (finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            && isEpisodicBeatStyle
            && priorPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax)
        {
            // Just entered Climax with an episodic theme: initialize cursor.
            v2State.CurrentBeatCode = "1a";
            v2State.TurnsInCurrentBeat = 0;
            _logger.LogInformation("ClimaxBeatCursor initialized: SessionId={SessionId} BeatCode=1a", session.Id);
        }
        else if (finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
                 && isEpisodicBeatStyle
                 && v2State.CurrentBeatCode == null)
        {
            // In Climax with episodic theme but cursor missing (session upgrade): recover to "1a".
            v2State.CurrentBeatCode = "1a";
            v2State.TurnsInCurrentBeat = 0;
            _logger.LogInformation("ClimaxBeatCursor recovered to 1a: SessionId={SessionId}", session.Id);
        }
        else if (finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
                 && isEpisodicBeatStyle
                 && v2State.CurrentBeatCode != null
                 && generatedSinceLastEval > 0
                 && _climaxBeatRepository != null)
        {
            // Still in Climax with new interactions: advance cursor by ONE beat if
            // threshold met. Multi-hop advancement is intentionally disabled � pacing
            // should walk the beat sheet one step at a time, even if multiple turns
            // batched into a single pipeline run.
            var beforeBeatCode = v2State.CurrentBeatCode;
            var beforeTurns = v2State.TurnsInCurrentBeat;
            v2State.TurnsInCurrentBeat += generatedSinceLastEval;
            var beatEntry = await _climaxBeatRepository.GetByCodeAsync(v2State.CurrentBeatCode, cancellationToken);
            string outcome;
            string? toBeatCode = null;
            if (beatEntry is null)
            {
                outcome = "BeatNotFound";
                _logger.LogWarning(
                    "ClimaxBeatCursor: BeatCode {BeatCode} not found in repository: SessionId={SessionId}",
                    v2State.CurrentBeatCode, session.Id);
            }
            else if (beatEntry.NextBeatCode is null)
            {
                outcome = "EndOfChain";
                // Clamp so TurnsInCurrentBeat does not grow unbounded at the terminal beat.
                if (v2State.TurnsInCurrentBeat > beatEntry.MinTurnsBeforeAdvance)
                    v2State.TurnsInCurrentBeat = beatEntry.MinTurnsBeforeAdvance;
            }
            else if (v2State.TurnsInCurrentBeat < beatEntry.MinTurnsBeforeAdvance)
            {
                outcome = "ThresholdNotMet";
            }
            else
            {
                outcome = "Advanced";
                toBeatCode = beatEntry.NextBeatCode;
                _logger.LogInformation(
                    "ClimaxBeatCursor advanced: {From} -> {To} after {Turns} turns in beat (threshold={Threshold}): SessionId={SessionId}",
                    v2State.CurrentBeatCode, beatEntry.NextBeatCode, v2State.TurnsInCurrentBeat, beatEntry.MinTurnsBeforeAdvance, session.Id);
                v2State.CurrentBeatCode = beatEntry.NextBeatCode;
                // Carry leftover turn credit forward into the next beat instead of zeroing it.
                // This guarantees that a submission generating N>1 interactions (e.g. user-prompt
                // + model continuation in a single PostUnifiedPromptSubmissionAsync call) does not
                // discard the extra turns; the next pipeline tick will see the leftover credit
                // and advance the next beat once its own threshold is met.
                v2State.TurnsInCurrentBeat -= beatEntry.MinTurnsBeforeAdvance;
                if (v2State.TurnsInCurrentBeat < 0)
                    v2State.TurnsInCurrentBeat = 0;
            }

            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "ClimaxBeatCursorTick",
                Severity = outcome == "BeatNotFound" ? "Warn" : "Info",
                Summary = outcome == "Advanced"
                    ? $"Beat cursor {beforeBeatCode} -> {toBeatCode} (turns {beforeTurns}+{generatedSinceLastEval}>=thr {beatEntry?.MinTurnsBeforeAdvance}, carryOver={v2State.TurnsInCurrentBeat})"
                    : $"Beat cursor {beforeBeatCode} stayed ({outcome}); turns {beforeTurns}+{generatedSinceLastEval}={v2State.TurnsInCurrentBeat}, threshold={(beatEntry is null ? "n/a" : beatEntry.MinTurnsBeforeAdvance.ToString())}",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    outcome,
                    beforeBeatCode,
                    afterBeatCode = v2State.CurrentBeatCode,
                    nextBeatCode = beatEntry?.NextBeatCode,
                    beforeTurns,
                    addedTurns = generatedSinceLastEval,
                    afterTurns = v2State.TurnsInCurrentBeat,
                    carryOverTurns = outcome == "Advanced" ? v2State.TurnsInCurrentBeat : 0,
                    minTurnsBeforeAdvance = beatEntry?.MinTurnsBeforeAdvance,
                    stageNumber = beatEntry?.StageNumber,
                    stageName = beatEntry?.StageName,
                    subBeatName = beatEntry?.SubBeatName,
                    previousLastEvaluationUtc = previousV2State?.LastEvaluationUtc,
                    enableAdaptiveStateUpdates = _enableAdaptiveStateUpdates
                })
            }, cancellationToken);
        }
        else if (finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
                 && isEpisodicBeatStyle
                 && v2State.CurrentBeatCode != null
                 && _climaxBeatRepository != null)
        {
            // Climax with episodic cursor present but no new interactions counted this run.
            // Emit a diagnostic so we can see why the cursor isn't ticking.
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord
            {
                SessionId = session.Id,
                EventKind = "ClimaxBeatCursorTick",
                Severity = "Info",
                Summary = $"Beat cursor {v2State.CurrentBeatCode} idle (generatedSinceLastEval=0)",
                MetadataJson = JsonSerializer.Serialize(new
                {
                    outcome = "NoNewInteractions",
                    beforeBeatCode = v2State.CurrentBeatCode,
                    afterBeatCode = v2State.CurrentBeatCode,
                    addedTurns = 0,
                    afterTurns = v2State.TurnsInCurrentBeat,
                    previousLastEvaluationUtc = previousV2State?.LastEvaluationUtc,
                    totalGeneratedInteractions,
                    previousPhaseInteractionCount,
                    enableAdaptiveStateUpdates = _enableAdaptiveStateUpdates
                })
            }, cancellationToken);
        }

        
        // ---- Multi-encounter Climax lifecycle -----------------------------------------------
        // Theme-scoped via [ClimaxMode:multi-encounter] marker. Dormant for all other themes.
        var isMultiEncounterClimax = RolePlayAssistantPrompts.IsMultiEncounterClimax(beatCursorTheme, "Climax");

        if (isMultiEncounterClimax
            && finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            && priorPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax)
        {
            await EnsureEncounterCompletedMappingAsync(beatCursorTheme!, session.Id, cancellationToken);
            v2State.CurrentEncounterNumber = 1;
            v2State.InteractionsInCurrentEncounter = 0;
            _logger.LogInformation("MultiEncounterClimax initialized: SessionId={SessionId} ThemeId={ThemeId} EncounterNumber=1", session.Id, v2State.ActiveScenarioId);
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord { SessionId = session.Id, EventKind = "MultiEncounterClimaxInitialized", Severity = "Info", Summary = $"Multi-encounter Climax initialized (theme={v2State.ActiveScenarioId}, encounter=1)", MetadataJson = JsonSerializer.Serialize(new { themeId = v2State.ActiveScenarioId, encounterNumber = 1, priorPhase = priorPhase?.ToString(), finalPhase = finalPhase.ToString() }) }, cancellationToken);
        }
        else if (isMultiEncounterClimax
            && finalPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            && priorPhase == DreamGenClone.Domain.RolePlay.NarrativePhase.Climax
            && generatedSinceLastEval > 0
            && v2State.CurrentTimeSkipPhase == TimeSkipPhase.None)
        {
            v2State.InteractionsInCurrentEncounter += generatedSinceLastEval;
        }
        else if (finalPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax && v2State.CurrentEncounterNumber != 0)
        {
            v2State.CurrentEncounterNumber = 0;
            v2State.InteractionsInCurrentEncounter = 0;
            _logger.LogInformation("MultiEncounterClimax cleared: SessionId={SessionId} (left Climax phase)", session.Id);
        }
// Refresh evaluation watermark so generatedSinceLastEval only counts interactions
        // created after this pipeline execution.
        v2State.LastEvaluationUtc = DateTime.UtcNow;

        await _stateRepository.SaveAdaptiveStateAsync(v2State, cancellationToken);
        SyncSessionAdaptiveStateFromV2(session, v2State);
    }

    private static DreamGenClone.Domain.RolePlay.AdaptiveScenarioState HydrateV2State(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState? previousState)
    {
        var mapped = session.AdaptiveState;
        mapped.SyncCharacterSnapshots();
        if (previousState is null)
        {
            return mapped;
        }

        var manualOverrideLocked = IsManualThemeOverrideLockActive(session);
        if (manualOverrideLocked)
        {
            mapped.ActiveScenarioId = session.AdaptiveState.ActiveScenarioId;
            mapped.ActiveVariantId = session.AdaptiveState.ActiveVariantId;
            mapped.CurrentPhase = session.AdaptiveState.CurrentPhase;
            mapped.InteractionCountInPhase = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        }
        else
        {
            // V2 ThemeSelectionRule is authoritative. PayloadJson can lag � e.g. retaining
            // "Observing" after the background job has advanced the tracker to
            // "ActiveScenarioLock". Using the stale PayloadJson value makes isObservingWindow=true,
            // which nulls ActiveScenarioId and triggers false scenario re-selection on every
            // pipeline run, resetting InteractionCountInPhase to 0 each time.
            //
            // Still respect the original guard: while the session is genuinely in its
            // observation window, do not restore ActiveScenarioId from the V2 snapshot.
            // The snapshot may hold the just-completed scenario from the previous cycle
            // (saved before ExecuteResetAsync cleared it). Restoring it here would defeat the
            // Reset?Observer?BuildUp re-selection flow. The reset pipeline explicitly writes
            // ThemeSelectionRule="Observing" into V2 (line ~2956), so V2 is correct for that case.
            if (!string.IsNullOrWhiteSpace(previousState.ThemeSelectionRule))
                mapped.ThemeSelectionRule = previousState.ThemeSelectionRule;
            var isObservingWindow = string.Equals(
                mapped.ThemeSelectionRule, "Observing",
                StringComparison.OrdinalIgnoreCase);
            mapped.ActiveScenarioId = isObservingWindow
                ? null
                : previousState.ActiveScenarioId ?? mapped.ActiveScenarioId;
            mapped.ActiveVariantId = previousState.ActiveVariantId ?? mapped.ActiveVariantId;
            mapped.CurrentPhase = previousState.CurrentPhase;
            mapped.InteractionCountInPhase = Math.Max(0, previousState.InteractionCountInPhase);
        }

        mapped.ConsecutiveLeadCount = Math.Max(0, previousState.ConsecutiveLeadCount);
        mapped.CycleIndex = Math.Max(mapped.CycleIndex, previousState.CycleIndex);
        mapped.ActiveFormulaVersion = string.IsNullOrWhiteSpace(previousState.ActiveFormulaVersion)
            ? mapped.ActiveFormulaVersion
            : previousState.ActiveFormulaVersion;
        mapped.SelectedNarrativeGateProfileId = previousState.SelectedNarrativeGateProfileId ?? mapped.SelectedNarrativeGateProfileId;
        // PhaseOverride fields are authoritative in the V2 live store (RolePlayV2AdaptiveStates).
        // null is a valid cleared state (set by ClearPhaseOverrideLock). Do NOT fall back to the
        // session payload here � it may be stale and would re-activate a cleared floor override.
        mapped.PhaseOverrideFloor = previousState.PhaseOverrideFloor;
        mapped.PhaseOverrideScenarioId = previousState.PhaseOverrideScenarioId;
        mapped.PhaseOverrideCycleIndex = previousState.PhaseOverrideCycleIndex;
        mapped.PhaseOverrideSource = previousState.PhaseOverrideSource;
        mapped.PhaseOverrideAppliedUtc = previousState.PhaseOverrideAppliedUtc;
        mapped.LastEvaluationUtc = previousState.LastEvaluationUtc;
        mapped.CurrentSceneLocation = previousState.CurrentSceneLocation;
        mapped.CharacterLocations = previousState.CharacterLocations
            .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationState
            {
                CharacterId = x.CharacterId,
                TrueLocation = x.TrueLocation,
                IsHidden = x.IsHidden,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();
        mapped.CharacterLocationPerceptions = previousState.CharacterLocationPerceptions
            .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
            {
                ObserverCharacterId = x.ObserverCharacterId,
                TargetCharacterId = x.TargetCharacterId,
                PerceivedLocation = x.PerceivedLocation,
                Confidence = x.Confidence,
                HasLineOfSight = x.HasLineOfSight,
                IsInProximity = x.IsInProximity,
                KnowledgeSource = x.KnowledgeSource,
                UpdatedUtc = x.UpdatedUtc
            })
            .ToList();
        // CharacterSnapshots: carry forward the V2-persisted values so stat deltas applied by
        // the background semantic analysis (ApplyInferredSemanticEvidenceAsync) are not
        // overwritten by the stale PayloadJson baseline when the foreground pipeline saves.
        // Must deep-copy to avoid shared-reference side effects between the V2 store and the
        // in-memory session state.
        mapped.CharacterSnapshots = previousState.CharacterSnapshots
            .Select(s => new DreamGenClone.Domain.RolePlay.CharacterStatProfileV2
            {
                CharacterId = s.CharacterId,
                Desire = s.Desire,
                Restraint = s.Restraint,
                Dominance = s.Dominance,
                Loyalty = s.Loyalty,
                SelfRespect = s.SelfRespect,
                SnapshotUtc = s.SnapshotUtc,
                BaselineStats = new Dictionary<string, int>(s.BaselineStats, StringComparer.OrdinalIgnoreCase),
                LastStatDeltas = new Dictionary<string, int>(s.LastStatDeltas, StringComparer.OrdinalIgnoreCase),
                LastStatDeltaUpdatedUtc = s.LastStatDeltaUpdatedUtc,
                UpdatedUtc = s.UpdatedUtc,
                RuntimeEncounterStats = s.RuntimeEncounterStats is not null
                    ? new Dictionary<string, int>(s.RuntimeEncounterStats, StringComparer.OrdinalIgnoreCase)
                    : null,
                CharacterRole = s.CharacterRole
            })
            .ToList();
        mapped.RebuildCharacterStatsCache();

        // FR-006: Persist multi-encounter time-skip state across save/load cycles.
        // Restore the persisted phase/encounter counters from the V2 snapshot so a
        // session reopened after browser close picks up exactly where it left off.
        // AlignPromptNarrativeStateWithV2Async intentionally skips these fields during
        // mid-pipeline reloads to avoid overwriting in-progress transitions — this
        // one-time restore in HydrateV2State is the authoritative load path.
        mapped.CurrentTimeSkipPhase = previousState.CurrentTimeSkipPhase;
        mapped.CurrentEncounterNumber = previousState.CurrentEncounterNumber;
        mapped.InteractionsInCurrentEncounter = previousState.InteractionsInCurrentEncounter;

        mapped.CurrentBeatCode = previousState.CurrentBeatCode;
        mapped.TurnsInCurrentBeat = previousState.TurnsInCurrentBeat;
        mapped.ThemeMachineSnapshot = previousState.ThemeMachineSnapshot is null
            ? null
            : CloneThemeMachineSnapshot(previousState.ThemeMachineSnapshot);

        // Theme scores and metadata: if the fresh V1 map has no themes yet (pre-seed),
        // carry forward the persisted V2 scores so display stays consistent.
        if (mapped.ThemeScores.Count == 0 && previousState.ThemeScores.Count > 0)
        {
            mapped.ThemeScores = previousState.ThemeScores;
            mapped.PrimaryThemeId ??= previousState.PrimaryThemeId;
            mapped.SecondaryThemeId ??= previousState.SecondaryThemeId;
            mapped.ThemeSelectionRule = previousState.ThemeSelectionRule;
            mapped.ObservedTurnCount = Math.Max(mapped.ObservedTurnCount, previousState.ObservedTurnCount);
            mapped.SelectionMinimumTurns = Math.Max(mapped.SelectionMinimumTurns, previousState.SelectionMinimumTurns);
            mapped.RecentEvidence = previousState.RecentEvidence;
        }
        else if (previousState.ThemeScores.Count > 0)
        {
            // When themes are already seeded in the current state, carry forward the accumulated
            // InteractionEvidenceSignal from the V2-persisted state. IES is incremented
            // exclusively by the async background semantic job. Without this merge every foreground
            // pipeline turn resets V2ThemeScores back to the stale PayloadJson baseline (IES=0),
            // causing background-job accumulations to be overwritten and IES to never rise above
            // a single per-run delta. The merge is monotone (only increases IES/Score) so it
            // cannot undo inline-regex contributions applied earlier in this turn.
            foreach (var (themeId, prevScore) in previousState.ThemeScores)
            {
                if (mapped.ThemeScores.TryGetValue(themeId, out var cur) &&
                    prevScore.Breakdown.InteractionEvidenceSignal > cur.Breakdown.InteractionEvidenceSignal)
                {
                    var iesDelta = prevScore.Breakdown.InteractionEvidenceSignal - cur.Breakdown.InteractionEvidenceSignal;
                    cur.Breakdown.InteractionEvidenceSignal = prevScore.Breakdown.InteractionEvidenceSignal;
                    cur.Score = Math.Clamp(cur.Score + iesDelta, 0, 100);
                }
            }
        }

        return mapped;
    }

    private static bool IsManualThemeOverrideLockActive(RolePlaySession session)
    {
        if (!string.Equals(session.AdaptiveState.ThemeSelectionRule, "ManualOverride", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(session.AdaptiveState.ActiveScenarioId))
        {
            return false;
        }

        var interactionCount = Math.Max(0, session.AdaptiveState.InteractionsSinceCommitment);
        return interactionCount < ManualOverrideSelectionLockInteractions;
    }

    private async Task AlignPromptNarrativeStateWithV2Async(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var snapshot = await _stateRepository.LoadAdaptiveStateAsync(session.Id, cancellationToken);
        if (snapshot is null)
        {
            return;
        }

        session.AdaptiveState.ActiveVariantId = snapshot.ActiveVariantId;
        session.AdaptiveState.CurrentPhase = snapshot.CurrentPhase;

        // While the session is in its observation window the in-memory tracker owns the
        // scenario slot: the observation window reset cleared ActiveScenarioId to null and
        // set ThemeSelectionRule="Observing". The V2 snapshot may still hold the just-
        // completed scenario from the previous cycle (written before ExecuteResetAsync
        // replaced session.AdaptiveState). Restoring it here would re-lock the tracker onto
        // the old scenario and defeat the observation period entirely.
        var isObservingWindow = string.Equals(
            session.AdaptiveState.ThemeSelectionRule, "Observing",
            StringComparison.OrdinalIgnoreCase);
        if (!isObservingWindow)
        {
            session.AdaptiveState.ActiveScenarioId = snapshot.ActiveScenarioId;
        }
        session.AdaptiveState.PhaseOverrideFloor = snapshot.PhaseOverrideFloor;
        session.AdaptiveState.PhaseOverrideScenarioId = snapshot.PhaseOverrideScenarioId;
        session.AdaptiveState.PhaseOverrideCycleIndex = snapshot.PhaseOverrideCycleIndex;
        session.AdaptiveState.PhaseOverrideSource = snapshot.PhaseOverrideSource;
        session.AdaptiveState.PhaseOverrideAppliedUtc = snapshot.PhaseOverrideAppliedUtc;

        // Multi-encounter Climax: CurrentTimeSkipPhase, CurrentEncounterNumber, and
        // InteractionsInCurrentEncounter are intentionally NOT synced from the V2 snapshot.
        // These fields are owned by the in-memory pipeline during overflow/generation and
        // must not be overwritten by the DB snapshot (which may lag behind the pipeline's
        // real-time phase transitions like CloseScene → AdvanceTime → None).

        var interactionCount = Math.Max(0, snapshot.InteractionCountInPhase);
        session.AdaptiveState.InteractionsSinceCommitment = snapshot.CurrentPhase == NarrativePhase.BuildUp
            ? 0
            : interactionCount;
        session.AdaptiveState.InteractionsInApproaching = snapshot.CurrentPhase == NarrativePhase.Approaching
            ? interactionCount
            : 0;
        session.AdaptiveState.ThemeMachineSnapshot = snapshot.ThemeMachineSnapshot;
        SyncThemeTrackerFromV2State(session, snapshot);

        // Carry forward character snapshots from the V2 table so that async semantic deltas
        // (written by SemanticInteractionAnalysisJobHandler) are not overwritten by the session's
        // stale PayloadJson copy at the start of the next turn.  Only replace when the V2 table
        // has entries; an empty list means this session has not been seeded yet and the in-memory
        // state (from SeedPersonaStatsFromTemplateAsync) is the correct source of truth.
        if (snapshot.CharacterSnapshots.Count > 0)
        {
            session.AdaptiveState.CharacterSnapshots = snapshot.CharacterSnapshots;
            session.AdaptiveState.RebuildCharacterStatsCache();
        }

        // Always refresh encounter summaries from the V2 table snapshot. The LLM enhancement
        // job writes LlmSummary asynchronously after the session was last saved to PayloadJson,
        // so the PayloadJson copy may be stale (LlmSummary=null). The V2 snapshot is the
        // authoritative, up-to-date source.
        if (snapshot.EncounterSummaries.Count > 0)
        {
            session.AdaptiveState.EncounterSummaries = snapshot.EncounterSummaries;
        }
    }

    private static void SyncSessionAdaptiveStateFromV2(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState v2State)
    {
        // In V2, session.AdaptiveState IS AdaptiveScenarioState. Just assign and rebuild cache.
        session.AdaptiveState = v2State;
        v2State.RebuildCharacterStatsCache();
    }

    private static void SyncThemeTrackerFromV2State(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState v2State)
    {
        // No-op: theme state is stored as flat fields on AdaptiveScenarioState.
        // session.AdaptiveState and v2State are the same object in V2.
    }

    private static string ResolveAdaptiveCharacterStatsKey(RolePlaySession session, string characterId)
    {
        var existing = session.AdaptiveState.CharacterStats
            .FirstOrDefault(x => string.Equals(x.Value.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(existing.Key))
        {
            return existing.Key;
        }

        var perspectiveMatch = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(x.CharacterName));
        if (perspectiveMatch is not null)
        {
            return perspectiveMatch.CharacterName.Trim();
        }

        return characterId;
    }

    private async Task<IReadOnlyDictionary<string, decimal>?> ResolveThemeStatDecayScaleOverridesAsync(
        string? themeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(themeId) || _rpThemeService is null)
        {
            return null;
        }

        var theme = await _rpThemeService.GetThemeAsync(themeId, cancellationToken);
        if (theme is null || theme.StatDecayOverrides.Count == 0)
        {
            return null;
        }

        return theme.StatDecayOverrides
            .Where(o => !string.IsNullOrWhiteSpace(o.StatName))
            .ToDictionary(
                o => o.StatName,
                o => Math.Clamp(o.DecayScale, 0m, 1m),
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task ApplyThemeSemiResetAsync(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState tracker, string? completedScenarioId, CancellationToken cancellationToken)
    {
        tracker.RecentEvidence.Clear();
        tracker.PrimaryThemeId = null;
        tracker.SecondaryThemeId = null;
        // "Observing" prevents NormalizeRolePlaySession from re-locking the scenario via
        // the repair path (which fires when ThemeSelectionRule != "Observing" and
        // ActiveScenarioId is null). RecalculateSelectedThemes will confirm "Observing"
        // while ObservedTurnCount <= SelectionMinimumTurns, then naturally advance to
        // Top1/Top2Blend once enough evidence accumulates.
        tracker.ThemeSelectionRule = "Observing";
        // Reset the observation counter so the next BuildUp re-enters observer mode and
        // accumulates fresh evidence before re-committing to a scenario. SelectionMinimumTurns
        // is already populated from the configured RP theme profile at session start and is
        // intentionally preserved here.
        tracker.ObservedTurnCount = 0;
        tracker.ThemeTrackerUpdatedUtc = DateTime.UtcNow;

        foreach (var item in tracker.ThemeScores.Values)
        {
            item.Breakdown.CharacterStateSignal = 0;
            item.Breakdown.SuccessorCausalityBoost = 0;
            item.Breakdown.CompletionFitScorePenalty = 0;
            item.Intensity = ResolveResetIntensity(item.Score);
        }

        if (!string.IsNullOrWhiteSpace(completedScenarioId)
            && tracker.ThemeScores.TryGetValue(completedScenarioId, out var completedTheme))
        {
            completedTheme.Score = Math.Round(Math.Max(0, completedTheme.Score - (double)_completedScenarioThemeScorePenalty), 4);
            completedTheme.Intensity = ResolveResetIntensity(completedTheme.Score);
            completedTheme.Breakdown.CompletionFitScorePenalty = (double)_completedScenarioFitScorePenaltyPoints;
        }

        if (string.IsNullOrWhiteSpace(completedScenarioId) || _rpThemeService is null)
        {
            return;
        }

        var completedThemeConfig = await _rpThemeService.GetThemeAsync(completedScenarioId, cancellationToken);
        if (completedThemeConfig is null)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 theme semi-reset causality failed for scenario '{completedScenarioId}': RP theme definition was not found.");
        }

        foreach (var successor in completedThemeConfig.SuccessorThemeLinks
                     .Where(link => !string.IsNullOrWhiteSpace(link.SuccessorThemeId))
                     .OrderBy(link => link.SortOrder))
        {
            if (!tracker.ThemeScores.TryGetValue(successor.SuccessorThemeId, out var successorTrackerItem))
            {
                continue;
            }

            successorTrackerItem.Score = Math.Round(Math.Max(0, successorTrackerItem.Score + (double)successor.ScoreBoost), 4);
            successorTrackerItem.Breakdown.InteractionEvidenceSignal = Math.Clamp(
                successorTrackerItem.Breakdown.InteractionEvidenceSignal + (double)successor.ScoreBoost, 0, 100);
            // Direct FitScore boost: applied additively to the gate-adjusted FitScore (0-100 scale)
            // during candidate evaluation. Persisted in BreakdownJson. Cleared on the next semi-reset.
            successorTrackerItem.Breakdown.SuccessorCausalityBoost += (double)successor.ScoreBoost;
            successorTrackerItem.Intensity = ResolveResetIntensity(successorTrackerItem.Score);
        }
    }

    private async Task EnsureEncounterCompletedMappingAsync(RPTheme theme, string sessionId, CancellationToken cancellationToken)
    {
        if (_rpThemeService is null)
            throw new InvalidOperationException($"MissingEncounterCompletedMapping: cannot verify encounter-completed mapping for theme '{theme.Id}' (session '{sessionId}') — IRPThemeService is unavailable.");
        var hasMapping = theme.SemanticEventMappings.Any(x => string.Equals(x.EventId, "encounter-completed", StringComparison.OrdinalIgnoreCase));
        if (!hasMapping)
        {
            var reloaded = await _rpThemeService.GetThemeAsync(theme.Id, cancellationToken);
            hasMapping = reloaded?.SemanticEventMappings.Any(x => string.Equals(x.EventId, "encounter-completed", StringComparison.OrdinalIgnoreCase)) ?? false;
            if (!hasMapping)
                throw new InvalidOperationException($"MissingEncounterCompletedMapping: theme '{theme.Id}' has [ClimaxMode:multi-encounter] in its Climax phase guidance but no 'encounter-completed' semantic event mapping. Add an encounter-completed mapping to the theme before using multi-encounter mode.");
        }
    }

    private async Task TryDetectEncounterBoundaryAsync(RolePlaySession session, RolePlayInteraction interaction, AdaptiveScenarioState state, CancellationToken cancellationToken)
    {
        if (state.CurrentPhase != DreamGenClone.Domain.RolePlay.NarrativePhase.Climax) return;
        if (state.CurrentEncounterNumber <= 0) return;
        if (_semanticEventInferenceService is null || _rpThemeService is null) return;
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId)) return;

        // FR-008: A pending time-skip phase means a boundary has already fired for the
        // current encounter. Re-detecting and overwriting the phase mid-transition would
        // skip the AdvanceTime leg or reset back to CloseScene, defeating the two-turn
        // split. Defer until the phase returns to None (both legs have completed).
        if (state.CurrentTimeSkipPhase != TimeSkipPhase.None)
        {
            _logger.LogDebug(
                "TryDetectEncounterBoundary: skipped — time-skip phase {Phase} pending for encounter #{Encounter}. SessionId={SessionId}",
                state.CurrentTimeSkipPhase, state.CurrentEncounterNumber, session.Id);
            return;
        }

        // ---- Gate: only detect boundaries for characters actively in the encounter ----
        var actorName = string.IsNullOrWhiteSpace(interaction.ActorName) ? "Unknown" : interaction.ActorName;
        if (!state.IsCharacterHavingSex(actorName))
        {
            return; // Character not in encounter — skip LLM call entirely
        }

        RPTheme? theme = null;
        try { theme = await _rpThemeService.GetThemeAsync(state.ActiveScenarioId, cancellationToken); }
        catch (Exception ex) { _logger.LogDebug(ex, "TryDetectEncounterBoundary: could not load theme {ThemeId}", state.ActiveScenarioId); }
        if (theme is null || !RolePlayAssistantPrompts.IsMultiEncounterClimax(theme, "Climax")) return;
        var mapping = theme.SemanticEventMappings.FirstOrDefault(x => string.Equals(x.EventId, "encounter-completed", StringComparison.OrdinalIgnoreCase));
        if (mapping is null) { _logger.LogWarning("TryDetectEncounterBoundary: theme {ThemeId} has [ClimaxMode:multi-encounter] but no encounter-completed mapping", theme.Id); return; }
        var cwSize = Math.Max(12, session.ContextWindowSize);
        var ixIdx = session.Interactions.FindIndex(x => string.Equals(x.Id, interaction.Id, StringComparison.OrdinalIgnoreCase));
        var ctxStart = ixIdx >= 0 ? Math.Max(0, ixIdx - cwSize) : Math.Max(0, session.Interactions.Count - cwSize);
        var ctxEnd = ixIdx >= 0 ? ixIdx : session.Interactions.Count;
        var ctx = session.Interactions.Skip(ctxStart).Take(Math.Max(0, ctxEnd - ctxStart)).Where(x => !x.IsExcluded).Select(x => $"[{x.InteractionType}] {x.ActorName}: {x.Content}").ToList();
        SemanticEventInferenceResult inf;
        try
        {
            inf = await _semanticEventInferenceService.InferAsync(new SemanticEventInferenceRequest { SessionId = session.Id, InteractionId = interaction.Id, ActorName = actorName, InteractionText = interaction.Content ?? string.Empty, ContextTurns = ctx, AllowedEventIds = ["encounter-completed"], EventDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["encounter-completed"] = "The CURRENT sexual encounter has reached its natural conclusion — either through climax (orgasm has occurred, bodies are spent, the tension has released and the scene settles into afterglow) OR through interruption (someone is about to walk in, the risk becomes too high, they are startled apart, they hear a sound and freeze — the encounter is cut short and they must separate or hide). Do NOT detect during mid-encounter escalation or at the moment of orgasm itself. Do NOT detect if sexual activity within the same encounter is still ongoing or building. Only detect when the encounter is clearly over — whether finished or interrupted." } }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TryDetectEncounterBoundary: inference failed SessionId={SessionId}", session.Id);
            await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord { SessionId = session.Id, InteractionId = interaction.Id, EventKind = "EncounterBoundaryDetectionFailed", Severity = "Warn", ActorName = interaction.ActorName, Summary = $"Detection failed: {ex.GetType().Name}: {ex.Message}", MetadataJson = JsonSerializer.Serialize(new { error = ex.Message, encounterNumberBefore = state.CurrentEncounterNumber }) }, cancellationToken);
            return;
        }
        if (!inf.Success) { _logger.LogWarning("TryDetectEncounterBoundary: inference non-success SessionId={SessionId}", session.Id); return; }
        var detected = inf.Events.FirstOrDefault(x => string.Equals(x.EventId, "encounter-completed", StringComparison.OrdinalIgnoreCase) && x.Confidence >= mapping.ConfidenceMin && x.Confidence <= mapping.ConfidenceMax);
        if (detected is null) { _logger.LogDebug("TryDetectEncounterBoundary: no detection SessionId={SessionId} Encounter={EncounterNumber}", session.Id, state.CurrentEncounterNumber); return; }
        const int minIxns = 4;
        if (state.InteractionsInCurrentEncounter < minIxns) { _logger.LogDebug("TryDetectEncounterBoundary: below minimum encounter length ({Current}/{Min}) SessionId={SessionId}", state.InteractionsInCurrentEncounter, minIxns, session.Id); return; }

        // ---- Keyword hard-gate: validate evidence span (skip for Instruction/System) ----
        if (interaction.InteractionType != InteractionType.System)
        {
            if (!ContainsEncounterCompletionKeywords(detected.EvidenceSpan))
            {
                _logger.LogWarning(
                    "TryDetectEncounterBoundary: rejected — evidence span lacks orgasm/interruption keywords. SessionId={SessionId} Actor={Actor} Confidence={Conf} EvidenceSpan=\"{Span}\"",
                    session.Id, actorName, detected.Confidence, detected.EvidenceSpan);
                return;
            }
        }

        var before = state.CurrentEncounterNumber;
        state.CurrentEncounterNumber++;
        state.InteractionsInCurrentEncounter = 0;
        state.CurrentTimeSkipPhase = TimeSkipPhase.CloseScene;

        // ---- Clear encounter participant set on boundary advance ----
        state.CharacterEncounterStates.Clear();

        // ---- Deferred persistence: mark dirty, flush at turn completion ----
        state.IsStateDirty = true;

        _logger.LogInformation("EncounterBoundaryAdvanced: SessionId={SessionId} {Before} -> {After} (conf={Conf})", session.Id, before, state.CurrentEncounterNumber, detected.Confidence);
        await _debugEventSink.WriteAsync(new RolePlayDebugEventRecord { SessionId = session.Id, InteractionId = interaction.Id, EventKind = "EncounterBoundaryAdvanced", Severity = "Info", ActorName = interaction.ActorName, Summary = $"Encounter boundary advanced: {before} -> {state.CurrentEncounterNumber} (conf={detected.Confidence})", MetadataJson = JsonSerializer.Serialize(new { encounterNumberBefore = before, encounterNumberAfter = state.CurrentEncounterNumber, confidence = detected.Confidence, evidenceSpan = detected.EvidenceSpan, themeId = theme.Id }) }, cancellationToken);
    }

    private static bool IsClimaxCompletionRequested(RolePlaySession session)
    {
        var latest = session.Interactions
            .Where(x => x.ParentInteractionId is null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest is null || string.IsNullOrWhiteSpace(latest.Content))
        {
            return false;
        }

        return ContainsClimaxCompletionCommand(latest.Content);
    }

    private static bool IsReturnBeatCompletionRequested(RolePlaySession session)
    {
        var latest = session.Interactions
            .Where(x => x.ParentInteractionId is null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();
        if (latest is null || string.IsNullOrWhiteSpace(latest.Content))
        {
            return false;
        }

        if (latest.InteractionType != InteractionType.System
            || !string.Equals(latest.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsReturnBeatCompletionCommand(latest.Content);
    }

    private async Task<ReturnBeatAutoDetectionResult> TryApplyAutomaticReturnBeatCompletionAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        if (state.ThemeMachineSnapshot is null
            || string.IsNullOrWhiteSpace(state.ThemeMachineSnapshot.CurrentStateCode)
            || state.ThemeMachineSnapshot.ReturnBeatCompleted
            || !IsReturnBeatCompletionEligibleState(state.ThemeMachineSnapshot.CurrentStateCode))
        {
            return ReturnBeatAutoDetectionResult.NotEvaluated;
        }

        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': active scenario id is missing.");
        }

        if (_themeMachineResolutionService is null)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': theme machine resolution service is required.");
        }

        var resolvedDefinition = await _themeMachineResolutionService.ResolveAsync(
            session.Id,
            state.ActiveScenarioId,
            state.ThemeMachineSnapshot,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': no machine definition resolved for active scenario '{state.ActiveScenarioId}'.");

        var detectionConfig = ResolveReturnBeatDetectionConfig(
            session.Id,
            state.ThemeMachineSnapshot.CurrentStateCode,
            resolvedDefinition.Transitions);

        var (transgressorRoleActorTargets, partnerRoleActorTargets) = await ResolveReturnBeatRoleActorTargetsAsync(
            session,
            state,
            detectionConfig.TransgressorRoleName,
            detectionConfig.PartnerRoleName,
            cancellationToken);

        var (applied, matchedSignal, sourceInteractionId) = TryApplyConfiguredReturnBeatCompletion(
            session,
            state,
            detectionConfig.CompletionSignals,
            transgressorRoleActorTargets,
            partnerRoleActorTargets);

        return new ReturnBeatAutoDetectionResult(
            Evaluated: true,
            Applied: applied,
            ConfiguredSignalCount: detectionConfig.CompletionSignals.Count,
            MatchedSignal: matchedSignal,
            SourceInteractionId: sourceInteractionId);
    }

    private static ReturnBeatDetectionConfig ResolveReturnBeatDetectionConfig(
        string sessionId,
        string currentStateCode,
        IReadOnlyList<RPThemeMachineTransition> transitions)
    {
        var cooldownTransitions = transitions
            .Where(x => x.IsEnabled
                && string.Equals(x.FromStateCode, "ReintegrationCooldown", StringComparison.OrdinalIgnoreCase)
                && string.Equals((x.TriggerType ?? string.Empty).Trim(), "cooldown-eligibility", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (cooldownTransitions.Count == 0)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': missing enabled cooldown-eligibility transition from ReintegrationCooldown.");
        }

        var signals = new List<string>();
        string? transgressorRoleName = null;
        string? partnerRoleName = null;
        var configuredRolePairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requiresReturnBeatCompletion = false;

        foreach (var transition in cooldownTransitions)
        {
            if (string.IsNullOrWhiteSpace(transition.GateConfigJson))
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': transition '{transition.TransitionId}' is missing required GateConfigJson.");
            }

            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(transition.GateConfigJson);
                root = doc.RootElement.Clone();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': transition '{transition.TransitionId}' has invalid GateConfigJson.",
                    ex);
            }

            if (!root.TryGetProperty("requireReturnBeatCompleted", out var requireReturnBeatCompletedProperty)
                || (requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.True
                    && requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.False))
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' is missing required boolean requireReturnBeatCompleted.");
            }

            var requireReturnBeatCompleted = requireReturnBeatCompletedProperty.GetBoolean();
            if (!requireReturnBeatCompleted)
            {
                continue;
            }

            requiresReturnBeatCompletion = true;

            if (!root.TryGetProperty("returnBeatCompletionSignals", out var signalArray)
                || signalArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' is missing required string array returnBeatCompletionSignals.");
            }

            var transitionSignals = new List<string>();
            foreach (var element in signalArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' has non-string returnBeatCompletionSignals entries.");
                }

                var signal = element.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(signal))
                {
                    throw new InvalidOperationException(
                        $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' has blank returnBeatCompletionSignals entries.");
                }

                transitionSignals.Add(signal);
            }

            if (transitionSignals.Count == 0)
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' requires at least one returnBeatCompletionSignals entry.");
            }

            var transitionTransgressorRole = ResolveRequiredReturnBeatRoleName(
                sessionId,
                transition.TransitionId,
                root,
                "returnBeatTransgressorRole");
            var transitionPartnerRole = ResolveRequiredReturnBeatRoleName(
                sessionId,
                transition.TransitionId,
                root,
                "returnBeatPartnerRole");

            if (string.Equals(transitionTransgressorRole, transitionPartnerRole, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transition.TransitionId}' must configure distinct returnBeatTransgressorRole and returnBeatPartnerRole values.");
            }

            configuredRolePairs.Add($"{transitionTransgressorRole}|{transitionPartnerRole}");
            transgressorRoleName ??= transitionTransgressorRole;
            partnerRoleName ??= transitionPartnerRole;

            signals.AddRange(transitionSignals);
        }

        if (!requiresReturnBeatCompletion)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': no enabled cooldown transition in ReintegrationCooldown requires return-beat completion while current state is '{currentStateCode}'.");
        }

        var distinctSignals = signals
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctSignals.Count == 0)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': no usable returnBeatCompletionSignals were configured.");
        }

        if (configuredRolePairs.Count == 0
            || string.IsNullOrWhiteSpace(transgressorRoleName)
            || string.IsNullOrWhiteSpace(partnerRoleName))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': missing required return-beat role pair configuration.");
        }

        if (configuredRolePairs.Count > 1)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transitions define conflicting return-beat role pairs.");
        }

        return new ReturnBeatDetectionConfig(
            CompletionSignals: distinctSignals,
            TransgressorRoleName: transgressorRoleName,
            PartnerRoleName: partnerRoleName);
    }

    private static string ResolveRequiredReturnBeatRoleName(
        string sessionId,
        string transitionId,
        JsonElement gateConfig,
        string propertyName)
    {
        if (!gateConfig.TryGetProperty(propertyName, out var roleProperty)
            || roleProperty.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transitionId}' is missing required string {propertyName}.");
        }

        var rawRoleName = roleProperty.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(rawRoleName))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transitionId}' has blank {propertyName}.");
        }

        var normalizedRoleName = CharacterRoleCatalog.Normalize(rawRoleName);
        if (string.Equals(normalizedRoleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': cooldown transition '{transitionId}' has invalid {propertyName}='{rawRoleName}'.");
        }

        return normalizedRoleName;
    }

    private static IReadOnlyList<string> ResolveReturnBeatCompletionSignals(
        string sessionId,
        string currentStateCode,
        IReadOnlyList<RPThemeMachineTransition> transitions)
    {
        return ResolveReturnBeatDetectionConfig(sessionId, currentStateCode, transitions).CompletionSignals;
    }

    private readonly record struct ReturnBeatDetectionConfig(
        IReadOnlyList<string> CompletionSignals,
        string TransgressorRoleName,
        string PartnerRoleName);

    private static (bool Applied, string? MatchedSignal, string? SourceInteractionId) TryApplyConfiguredReturnBeatCompletion(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlyList<string> completionSignals,
        IReadOnlySet<string> transgressorRoleActorTargets,
        IReadOnlySet<string> partnerRoleActorTargets)
    {
        if (completionSignals.Count == 0)
        {
            throw new InvalidOperationException("Return beat completion signals are required for auto-detection.");
        }

        if (transgressorRoleActorTargets.Count == 0)
        {
            throw new InvalidOperationException("Return beat auto-detection requires at least one transgressor role actor target.");
        }

        if (partnerRoleActorTargets.Count == 0)
        {
            throw new InvalidOperationException("Return beat auto-detection requires at least one partner role actor target.");
        }

        var recentNarrative = GetRecentNarrativeInteractionsForReturnBeatDetection(session);
        if (recentNarrative.Count == 0)
        {
            return (false, null, null);
        }

        if (TryDetectDirectTransgressorPartnerDialogue(
                session,
                state,
                recentNarrative,
                transgressorRoleActorTargets,
                partnerRoleActorTargets,
                out var directDialogueInteractionId))
        {
            var applied = TryApplyExplicitReturnBeatCompletion(state);
            return (applied, "transgressor-partner-direct-dialogue", directDialogueInteractionId);
        }

        var sameScene = AreTransgressorAndPartnerInSameScene(state, transgressorRoleActorTargets, partnerRoleActorTargets);
        var inImmediateVicinity = IsTransgressorInImmediateVicinityOfPartner(state, transgressorRoleActorTargets, partnerRoleActorTargets);
        if (!sameScene && !inImmediateVicinity)
        {
            return (false, null, null);
        }

        if (!TryDetectPartnerAcknowledgement(
                session,
                state,
                recentNarrative,
                completionSignals,
                transgressorRoleActorTargets,
                partnerRoleActorTargets,
                out var acknowledgementSignal,
                out var acknowledgementInteractionId))
        {
            return (false, null, null);
        }

        var acknowledgedApplied = TryApplyExplicitReturnBeatCompletion(state);
        return (acknowledgedApplied, acknowledgementSignal, acknowledgementInteractionId);
    }

    private async Task<(IReadOnlySet<string> TransgressorRoleActorTargets, IReadOnlySet<string> PartnerRoleActorTargets)> ResolveReturnBeatRoleActorTargetsAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string transgressorRoleName,
        string partnerRoleName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedTransgressorRole = CharacterRoleCatalog.Normalize(transgressorRoleName);
        var normalizedPartnerRole = CharacterRoleCatalog.Normalize(partnerRoleName);
        if (string.Equals(normalizedTransgressorRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': transgressor role is invalid.");
        }

        if (string.Equals(normalizedPartnerRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': partner role is invalid.");
        }

        if (string.Equals(normalizedTransgressorRole, normalizedPartnerRole, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': transgressor and partner roles must be different.");
        }

        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{session.Id}': active scenario id is required to resolve return-beat role actors.");
        }

        var activeScenarioId = state.ActiveScenarioId.Trim();
        var candidateEvaluations = await _stateRepository.LoadCandidateEvaluationsAsync(session.Id, 50, cancellationToken);
        var (transgressorActorId, partnerActorId) = ResolveReturnBeatRoleBindingsFromEvaluations(
            session.Id,
            activeScenarioId,
            normalizedTransgressorRole,
            normalizedPartnerRole,
            candidateEvaluations);

        var transgressorTargets = BuildReturnBeatActorTargetsForBoundActorId(session, state, transgressorActorId, normalizedTransgressorRole);
        var partnerTargets = BuildReturnBeatActorTargetsForBoundActorId(session, state, partnerActorId, normalizedPartnerRole);

        return (transgressorTargets, partnerTargets);
    }

    private static (string TransgressorActorId, string PartnerActorId) ResolveReturnBeatRoleBindingsFromEvaluations(
        string sessionId,
        string activeScenarioId,
        string transgressorRoleName,
        string partnerRoleName,
        IReadOnlyList<ScenarioCandidateEvaluation> candidateEvaluations)
    {
        if (candidateEvaluations.Count == 0)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': no candidate evaluations are available to resolve role bindings for active scenario '{activeScenarioId}'.");
        }

        var latestScenarioEvaluation = candidateEvaluations
            .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioId)
                && string.Equals(x.ScenarioId.Trim(), activeScenarioId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.EvaluatedUtc)
            .FirstOrDefault();

        if (latestScenarioEvaluation is null)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': no candidate evaluation for active scenario '{activeScenarioId}' is available to resolve role bindings.");
        }

        if (string.IsNullOrWhiteSpace(latestScenarioEvaluation.DetailsJson))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{latestScenarioEvaluation.EvaluationId}' has empty DetailsJson role binding payload.");
        }

        JsonElement detailsRoot;
        try
        {
            using var detailsDocument = JsonDocument.Parse(latestScenarioEvaluation.DetailsJson);
            detailsRoot = detailsDocument.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{latestScenarioEvaluation.EvaluationId}' has invalid DetailsJson role binding payload.",
                ex);
        }

        if (!TryGetPropertyIgnoreCase(detailsRoot, "fitResult", out var fitResult)
            || fitResult.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{latestScenarioEvaluation.EvaluationId}' is missing fitResult role binding payload.");
        }

        if (!TryGetPropertyIgnoreCase(fitResult, "roleCharacterBindings", out var roleCharacterBindings)
            || roleCharacterBindings.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{latestScenarioEvaluation.EvaluationId}' is missing fitResult.roleCharacterBindings payload.");
        }

        var transgressorActorId = ResolveRequiredRoleCharacterBinding(
            sessionId,
            latestScenarioEvaluation.EvaluationId,
            roleCharacterBindings,
            transgressorRoleName);
        var partnerActorId = ResolveRequiredRoleCharacterBinding(
            sessionId,
            latestScenarioEvaluation.EvaluationId,
            roleCharacterBindings,
            partnerRoleName);

        if (string.Equals(transgressorActorId, partnerActorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{latestScenarioEvaluation.EvaluationId}' resolved identical actor binding '{transgressorActorId}' for roles '{transgressorRoleName}' and '{partnerRoleName}'.");
        }

        return (transgressorActorId, partnerActorId);
    }

    private static string ResolveRequiredRoleCharacterBinding(
        string sessionId,
        string evaluationId,
        JsonElement roleCharacterBindings,
        string roleName)
    {
        foreach (var binding in roleCharacterBindings.EnumerateObject())
        {
            if (!string.Equals(binding.Name, roleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (binding.Value.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{evaluationId}' roleCharacterBindings['{roleName}'] must be a string.");
            }

            var rawActorId = binding.Value.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(rawActorId))
            {
                throw new InvalidOperationException(
                    $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{evaluationId}' roleCharacterBindings['{roleName}'] must be non-empty.");
            }

            return rawActorId;
        }

        throw new InvalidOperationException(
            $"RolePlayV2 return-beat auto-detection failed for session '{sessionId}': candidate evaluation '{evaluationId}' has no roleCharacterBindings entry for role '{roleName}'.");
    }

    private static HashSet<string> BuildReturnBeatActorTargetsForBoundActorId(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string boundActorId,
        string roleName)
    {
        var normalizedBoundActorId = (boundActorId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedBoundActorId))
        {
            throw new InvalidOperationException(
                $"Return beat auto-detection failed to resolve a non-empty actor binding for role '{roleName}'.");
        }

        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddReturnBeatActorTarget(targets, normalizedBoundActorId);

        foreach (var perspective in session.CharacterPerspectives)
        {
            var perspectiveCharacterId = (perspective.CharacterId ?? string.Empty).Trim();
            var perspectiveCharacterName = (perspective.CharacterName ?? string.Empty).Trim();
            if (!string.Equals(perspectiveCharacterId, normalizedBoundActorId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(perspectiveCharacterName, normalizedBoundActorId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddReturnBeatActorTarget(targets, perspective.CharacterId);
            AddReturnBeatActorTarget(targets, perspective.CharacterName);
        }

        foreach (var snapshot in state.CharacterSnapshots)
        {
            if (string.Equals((snapshot.CharacterId ?? string.Empty).Trim(), normalizedBoundActorId, StringComparison.OrdinalIgnoreCase)
                || IsReturnBeatActorTarget(targets, snapshot.CharacterId))
            {
                AddReturnBeatActorTarget(targets, snapshot.CharacterId);
            }
        }

        foreach (var entry in session.AdaptiveState.CharacterStats)
        {
            var characterKey = (entry.Key ?? string.Empty).Trim();
            var blockCharacterId = (entry.Value?.CharacterId ?? string.Empty).Trim();
            if (!string.Equals(characterKey, normalizedBoundActorId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(blockCharacterId, normalizedBoundActorId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddReturnBeatActorTarget(targets, characterKey);
            AddReturnBeatActorTarget(targets, blockCharacterId);
        }

        if (string.Equals((session.PersonaName ?? string.Empty).Trim(), normalizedBoundActorId, StringComparison.OrdinalIgnoreCase))
        {
            AddReturnBeatActorTarget(targets, session.PersonaName);
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException(
                $"Return beat auto-detection requires non-empty actor targets for role '{roleName}'.");
        }

        return targets;
    }

    private static IReadOnlyList<RolePlayInteraction> GetRecentNarrativeInteractionsForReturnBeatDetection(
        RolePlaySession session,
        int take = 12)
    {
        return session.Interactions
            .Where(x => x.ParentInteractionId is null
                && !x.IsExcluded
                && !x.IsHidden
                && x.InteractionType is InteractionType.Npc or InteractionType.Custom or InteractionType.User
                && !string.IsNullOrWhiteSpace(x.Content))
            .OrderBy(x => x.CreatedAt)
            .TakeLast(Math.Max(1, take))
            .ToList();
    }

    private static bool TryDetectDirectTransgressorPartnerDialogue(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlyList<RolePlayInteraction> orderedInteractions,
        IReadOnlySet<string> transgressorRoleActorTargets,
        IReadOnlySet<string> partnerRoleActorTargets,
        out string? sourceInteractionId)
    {
        sourceInteractionId = null;

        for (var i = 1; i < orderedInteractions.Count; i++)
        {
            var previous = orderedInteractions[i - 1];
            var current = orderedInteractions[i];

            var previousIsTransgressor = IsInteractionFromRoleActor(session, state, previous, transgressorRoleActorTargets);
            var previousIsPartner = IsInteractionFromRoleActor(session, state, previous, partnerRoleActorTargets);
            var currentIsTransgressor = IsInteractionFromRoleActor(session, state, current, transgressorRoleActorTargets);
            var currentIsPartner = IsInteractionFromRoleActor(session, state, current, partnerRoleActorTargets);

            if ((previousIsTransgressor && currentIsPartner) || (previousIsPartner && currentIsTransgressor))
            {
                sourceInteractionId = current.Id;
                return true;
            }
        }

        return false;
    }

    private static bool TryDetectPartnerAcknowledgement(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlyList<RolePlayInteraction> orderedInteractions,
        IReadOnlyList<string> completionSignals,
        IReadOnlySet<string> transgressorRoleActorTargets,
        IReadOnlySet<string> partnerRoleActorTargets,
        out string? acknowledgementSignal,
        out string? acknowledgementInteractionId)
    {
        acknowledgementSignal = null;
        acknowledgementInteractionId = null;

        for (var i = 1; i < orderedInteractions.Count; i++)
        {
            var previous = orderedInteractions[i - 1];
            var current = orderedInteractions[i];

            var previousIsTransgressor = IsInteractionFromRoleActor(session, state, previous, transgressorRoleActorTargets);
            var currentIsPartner = IsInteractionFromRoleActor(session, state, current, partnerRoleActorTargets);

            if (previousIsTransgressor && currentIsPartner)
            {
                acknowledgementSignal = "partner-acknowledged-return";
                acknowledgementInteractionId = current.Id;
                return true;
            }
        }

        for (var i = orderedInteractions.Count - 1; i >= 0; i--)
        {
            var interaction = orderedInteractions[i];
            if (!IsInteractionFromRoleActor(session, state, interaction, partnerRoleActorTargets))
            {
                continue;
            }

            var configuredSignal = FindFirstConfiguredReturnBeatSignal(interaction.Content, completionSignals);
            if (string.IsNullOrWhiteSpace(configuredSignal))
            {
                continue;
            }

            acknowledgementSignal = configuredSignal;
            acknowledgementInteractionId = interaction.Id;
            return true;
        }

        return false;
    }

    private static bool IsInteractionFromRoleActor(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        RolePlayInteraction interaction,
        IReadOnlySet<string> roleActorTargets)
    {
        if (IsReturnBeatActorTarget(roleActorTargets, interaction.ActorName))
        {
            return true;
        }

        var resolvedActorId = ResolveDecisionActorId(state, session, interaction.ActorName);
        return IsReturnBeatActorTarget(roleActorTargets, resolvedActorId);
    }

    private static bool AreTransgressorAndPartnerInSameScene(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlySet<string> transgressorRoleActorTargets,
        IReadOnlySet<string> partnerRoleActorTargets)
    {
        var transgressorLocations = ResolveRoleActorLocations(state, transgressorRoleActorTargets);
        var partnerLocations = ResolveRoleActorLocations(state, partnerRoleActorTargets);

        if (transgressorLocations.Count == 0 || partnerLocations.Count == 0)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(state.CurrentSceneLocation))
        {
            var currentScene = state.CurrentSceneLocation.Trim();
            return transgressorLocations.Contains(currentScene)
                && partnerLocations.Contains(currentScene);
        }

        return transgressorLocations.Overlaps(partnerLocations);
    }

    private static bool IsTransgressorInImmediateVicinityOfPartner(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlySet<string> transgressorRoleActorTargets,
        IReadOnlySet<string> partnerRoleActorTargets)
    {
        return state.CharacterLocationPerceptions.Any(perception =>
            IsReturnBeatActorTarget(partnerRoleActorTargets, perception.ObserverCharacterId)
            && IsReturnBeatActorTarget(transgressorRoleActorTargets, perception.TargetCharacterId)
            && (perception.IsInProximity || perception.HasLineOfSight));
    }

    private static HashSet<string> ResolveRoleActorLocations(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        IReadOnlySet<string> roleActorTargets)
    {
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in state.CharacterLocations)
        {
            if (!IsReturnBeatActorTarget(roleActorTargets, location.CharacterId)
                || string.IsNullOrWhiteSpace(location.TrueLocation))
            {
                continue;
            }

            locations.Add(location.TrueLocation.Trim());
        }

        return locations;
    }

    private static bool IsReturnBeatActorTarget(IReadOnlySet<string> targets, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        return targets.Contains(candidate.Trim());
    }

    private static void AddReturnBeatActorTarget(ISet<string> targets, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        targets.Add(value.Trim());
    }

    private static string? FindFirstConfiguredReturnBeatSignal(string? content, IReadOnlyList<string> completionSignals)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var configuredSignal in completionSignals)
        {
            if (string.IsNullOrWhiteSpace(configuredSignal))
            {
                continue;
            }

            if (content.Contains(configuredSignal, StringComparison.OrdinalIgnoreCase))
            {
                return configuredSignal;
            }
        }

        return null;
    }

    private static bool ContainsClimaxCompletionCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        return content.Contains("/completeclimax", StringComparison.OrdinalIgnoreCase)
            || content.Contains("/endclimax", StringComparison.OrdinalIgnoreCase)
            || content.Contains("/end-climax", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(content, @"\b(complete|end)\s+climax\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool ContainsReturnBeatCompletionCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token =>
            string.Equals(token, "/returnbeat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "/return-beat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "/returnbeatcomplete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "/return-beat-complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "/returnbeatdone", StringComparison.OrdinalIgnoreCase)
            || string.Equals(token, "/return-beat-done", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryApplyExplicitReturnBeatCompletion(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        if (state.ThemeMachineSnapshot is null || string.IsNullOrWhiteSpace(state.ThemeMachineSnapshot.CurrentStateCode))
        {
            return false;
        }

        if (!IsReturnBeatCompletionEligibleState(state.ThemeMachineSnapshot.CurrentStateCode))
        {
            return false;
        }

        if (state.ThemeMachineSnapshot.ReturnBeatCompleted)
        {
            return false;
        }

        state.ThemeMachineSnapshot.ReturnBeatCompleted = true;
        return true;
    }

    private static bool IsReturnBeatCompletionEligibleState(string? currentStateCode)
    {
        if (string.IsNullOrWhiteSpace(currentStateCode))
        {
            return false;
        }

        return string.Equals(currentStateCode, "ReturnBeatRequired", StringComparison.OrdinalIgnoreCase)
            || string.Equals(currentStateCode, "ReintegrationCooldown", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ReturnBeatAutoDetectionResult(
        bool Evaluated,
        bool Applied,
        int ConfiguredSignalCount,
        string? MatchedSignal,
        string? SourceInteractionId)
    {
        public static ReturnBeatAutoDetectionResult NotEvaluated { get; } = new(
            Evaluated: false,
            Applied: false,
            ConfiguredSignalCount: 0,
            MatchedSignal: null,
            SourceInteractionId: null);
    }

    private static DreamGenClone.Domain.RolePlay.NarrativePhase? ResolveManualPhaseAdvanceTarget(
        string? content,
        DreamGenClone.Domain.RolePlay.NarrativePhase currentPhase)
    {
        if (!ContainsNextPhaseCommand(content))
        {
            return null;
        }

        // Climax exits ONLY via /endclimax � /nextphase is intentionally blocked here.
        return currentPhase switch
        {
            NarrativePhase.BuildUp => NarrativePhase.Committed,
            NarrativePhase.Committed => NarrativePhase.Approaching,
            NarrativePhase.Approaching => NarrativePhase.Climax,
            _ => null
        };
    }

    private static bool ContainsNextPhaseCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token => string.Equals(token, "/nextphase", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsSteerCommand(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var tokens = content
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Any(token => string.Equals(token, "/steer", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when any Instruction actor interaction within the last <paramref name="windowSize"/>
    /// entries has no GeneratedByCommand set (i.e., was a user-authored instruction, not engine-injected).
    /// </summary>
    private static bool HasRecentUserInstruction(RolePlaySession session, int windowSize)
    {
        return session.Interactions
            .TakeLast(windowSize)
            .Any(x => string.Equals(x.ActorName, "Instruction", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(x.GeneratedByCommand));
    }

    private static bool TryExtractSteerDirective(string? content, out string directive)
    {
        directive = string.Empty;
        if (!ContainsSteerCommand(content))
        {
            return false;
        }

        var raw = content?.Trim() ?? string.Empty;
        var markerIndex = raw.IndexOf("/steer", StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            directive = "Steer the scene in a meaningful, phase-consistent direction.";
            return true;
        }

        var remaining = raw[(markerIndex + "/steer".Length)..].Trim();
        directive = string.IsNullOrWhiteSpace(remaining)
            ? "Steer the scene in a meaningful, phase-consistent direction."
            : remaining;
        return true;
    }

    private static bool TryExtractClimaxCompletionDirective(string? content, out string directive)
    {
        directive = string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var raw = content.Trim();
        // Try each known command marker in order; strip whichever is present.
        string[] markers = ["/completeclimax", "/endclimax", "/end-climax"];
        foreach (var marker in markers)
        {
            var markerIndex = raw.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                directive = raw[(markerIndex + marker.Length)..].Trim();
                return true;
            }
        }

        // Natural-language variant ("complete climax" / "end climax").
        var match = Regex.Match(raw, @"\b(complete|end)\s+climax\b\s*(?<rest>.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (match.Success)
        {
            directive = match.Groups["rest"].Value.Trim();
            return true;
        }

        return false;
    }

    private static bool TryGetActivePhaseOverrideFloor(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        out DreamGenClone.Domain.RolePlay.NarrativePhase floor)
    {
        floor = DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp;
        if (!state.PhaseOverrideFloor.HasValue
            || !state.PhaseOverrideCycleIndex.HasValue
            || string.IsNullOrWhiteSpace(state.PhaseOverrideScenarioId)
            || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        if (state.PhaseOverrideCycleIndex.Value != state.CycleIndex
            || !string.Equals(state.PhaseOverrideScenarioId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        floor = state.PhaseOverrideFloor.Value;
        return true;
    }

    private static void NormalizePhaseOverrideLock(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        if (TryGetActivePhaseOverrideFloor(state, out _))
        {
            return;
        }

        ClearPhaseOverrideLock(state);
    }

    private static void ClearPhaseOverrideLock(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        state.PhaseOverrideFloor = null;
        state.PhaseOverrideScenarioId = null;
        state.PhaseOverrideCycleIndex = null;
        state.PhaseOverrideSource = null;
        state.PhaseOverrideAppliedUtc = null;
    }

    private static bool IsForwardPhaseTransition(
        DreamGenClone.Domain.RolePlay.NarrativePhase from,
        DreamGenClone.Domain.RolePlay.NarrativePhase to)
        => GetPhaseOrder(to) > GetPhaseOrder(from);

    private static int GetPhaseOrder(DreamGenClone.Domain.RolePlay.NarrativePhase phase)
        => phase switch
        {
            DreamGenClone.Domain.RolePlay.NarrativePhase.Opening => -1,
            DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp => 0,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Committed => 1,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching => 2,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Climax => 3,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Reset => 4,
            _ => 0
        };

    private static string ResolveResetIntensity(double score)
    {
        if (score >= 45)
        {
            return "Moderate";
        }

        if (score >= 15)
        {
            return "Minor";
        }

        return "None";
    }

    private async Task<bool> HasPendingDecisionPointAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        var points = await _stateRepository.LoadDecisionPointsAsync(session.Id, 30, cancellationToken);
        if (points.Count == 0)
        {
            return false;
        }

        var appliedIds = session.AppliedDecisionPointIds ??= [];
        return points.Any(x => !appliedIds.Contains(x.DecisionPointId, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<bool> HasRecentDecisionPointForContextAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        DecisionTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return false;
        }

        var points = await _stateRepository.LoadDecisionPointsAsync(session.Id, 10, cancellationToken);
        if (points.Count == 0)
        {
            return false;
        }

        var latest = points[^1];
        var sameContext = string.Equals(latest.ScenarioId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase)
            && latest.Phase == state.CurrentPhase
            && string.Equals(latest.TriggerSource, trigger.ToString(), StringComparison.OrdinalIgnoreCase);

        if (!sameContext)
        {
            return false;
        }

        return DateTime.UtcNow - latest.CreatedUtc < DecisionPointContextCooldown;
    }

    private bool IsDecisionTriggerEligible(
        DecisionTrigger trigger,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        if (trigger == DecisionTrigger.PhaseChanged && !_enablePhaseChangeDecisionPrompts)
        {
            return false;
        }

        if (trigger == DecisionTrigger.SceneLocationChanged && !_enableSceneLocationDecisionPrompts)
        {
            return false;
        }

        return trigger == DecisionTrigger.PhaseChanged
            || trigger == DecisionTrigger.SignificantStatChange
            || trigger == DecisionTrigger.CharacterDirectQuestion
            || trigger == DecisionTrigger.SceneLocationChanged
            || (trigger == DecisionTrigger.InteractionStart
                && state.InteractionCountInPhase > 0
                && state.InteractionCountInPhase % 3 == 0)
            || trigger == DecisionTrigger.ManualOverride;
    }

    private static bool HasSignificantStatChange(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState? previous,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState current,
        int threshold = 10)
    {
        if (previous is null || previous.CharacterSnapshots.Count == 0)
        {
            return false;
        }

        var previousByCharacter = previous.CharacterSnapshots
            .GroupBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var snapshot in current.CharacterSnapshots)
        {
            if (!previousByCharacter.TryGetValue(snapshot.CharacterId, out var old))
            {
                continue;
            }

            if (Math.Abs(snapshot.Desire - old.Desire) >= threshold
                || Math.Abs(snapshot.Restraint - old.Restraint) >= threshold
                || Math.Abs((snapshot.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50) - (old.RuntimeEncounterStats?.GetValueOrDefault("Tension") ?? 50)) >= threshold
                || Math.Abs((snapshot.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50) - (old.RuntimeEncounterStats?.GetValueOrDefault("Connection") ?? 50)) >= threshold
                || Math.Abs(snapshot.Dominance - old.Dominance) >= threshold
                || Math.Abs(snapshot.Loyalty - old.Loyalty) >= threshold
                || Math.Abs(snapshot.SelfRespect - old.SelfRespect) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveDecisionOptionDisplayText(string optionId)
    {
        if (DecisionOptionCatalog.TryGetValue(optionId, out var details))
        {
            return details.Label;
        }

        return optionId;
    }

    private static string ResolveDecisionOptionDeltaMap(string optionId)
    {
        if (DecisionOptionCatalog.TryGetValue(optionId, out var details) && details.Deltas.Count > 0)
        {
            return JsonSerializer.Serialize(details.Deltas);
        }

        return "{}";
    }

    private static string ResolveDecisionOptionPrerequisites(string optionId)
    {
        return optionId switch
        {
            "lean-in" => "{\"min\":{\"Desire\":55}}",
            "test-boundary" => "{\"min\":{\"Desire\":65},\"max\":{\"Loyalty\":70}}",
            "husband-observes" => "{\"min\":{\"Desire\":60}}",
            _ => "{}"
        };
    }

    private DreamGenClone.Domain.RolePlay.DecisionOption BuildDecisionOptionForContext(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string optionId)
    {
        var displayText = BuildDecisionAnswerChoiceText(optionId, decisionPoint);
        var deltaMap = ResolveDecisionOptionDeltaMap(optionId);
        var baseDeltas = ParseDeltaMap(deltaMap);
        var deltas = AdjustDecisionDeltasForContext(session, decisionPoint.TargetActorId, baseDeltas);
        var adjustedDeltaMap = JsonSerializer.Serialize(deltas);
        var topThemes = ResolveTopThemeNames(session, 2);
        var targetActorLabel = ResolveDecisionActorDisplayLabel(session, decisionPoint.TargetActorId);
        var askingActorLabel = ResolveDecisionActorDisplayLabel(session, decisionPoint.AskingActorName);
        var (highestStatName, highestStatValue) = ResolveHighestStat(session, decisionPoint.TargetActorId);
        var deescalating = IsDeescalatingChoice(deltas, highestStatName);

        return new DreamGenClone.Domain.RolePlay.DecisionOption
        {
            OptionId = optionId,
            DecisionPointId = decisionPoint.DecisionPointId,
            DisplayText = displayText,
            ResponsePreview = BuildDecisionResponsePreview(
                optionId,
                decisionPoint,
                targetActorLabel,
                askingActorLabel,
                topThemes),
            BehaviorStyleHint = BuildBehaviorStyleHint(deescalating, highestStatName, highestStatValue),
            CharacterDirectionInstruction = BuildCharacterDirectionInstruction(
                optionId,
                decisionPoint,
                targetActorLabel,
                askingActorLabel,
                topThemes,
                highestStatName,
                highestStatValue,
                deescalating),
            ChatInstruction = BuildChatInstruction(
                optionId,
                decisionPoint,
                topThemes,
                highestStatName,
                highestStatValue,
                deescalating),
            VisibilityMode = decisionPoint.TransparencyMode,
            Prerequisites = ResolveDecisionOptionPrerequisites(optionId),
            StatDeltaMap = adjustedDeltaMap,
            IsCustomResponseFallback = string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyDictionary<string, int> AdjustDecisionDeltasForContext(
        RolePlaySession session,
        string? targetActorId,
        IReadOnlyDictionary<string, int> baseDeltas)
    {
        if (!baseDeltas.TryGetValue("Restraint", out var restraintDelta) || restraintDelta >= 0)
        {
            return baseDeltas;
        }

        var currentRestraint = ResolveDecisionActorStatValue(session, targetActorId, "Restraint", AdaptiveStatCatalog.DefaultValue);
        var scale = currentRestraint switch
        {
            >= 90 => 0.80,
            >= 80 => 0.65,
            >= 65 => 0.45,
            _ => 0.30
        };

        var adjustedRestraintDelta = (int)Math.Round(restraintDelta * scale, MidpointRounding.AwayFromZero);
        adjustedRestraintDelta = Math.Min(-1, adjustedRestraintDelta);

        if (adjustedRestraintDelta == restraintDelta)
        {
            return baseDeltas;
        }

        var mutable = new Dictionary<string, int>(baseDeltas, StringComparer.OrdinalIgnoreCase)
        {
            ["Restraint"] = adjustedRestraintDelta
        };

        return mutable;
    }

    private static int ResolveDecisionActorStatValue(
        RolePlaySession session,
        string? actorId,
        string statName,
        int fallback)
    {
        CharacterStatProfileV2? statBlock = null;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            statBlock = session.AdaptiveState.CharacterStats.Values
                .FirstOrDefault(x => string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
        }

        statBlock ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (statBlock is null)
        {
            return fallback;
        }

        return CharacterStatProfileV2Accessor.GetStatOrDefault(statBlock, statName, fallback);
    }

    private async Task<DecisionOptionRewriteResult> TryApplyAiDecisionOptionAnswersAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        List<DreamGenClone.Domain.RolePlay.DecisionOption> options,
        CancellationToken cancellationToken)
    {
        if (_completionClient is null || _modelResolutionService is null)
        {
            return new DecisionOptionRewriteResult(options, false, "skipped", "model-services-unavailable");
        }

        var rewriteCandidates = options
            .Where(x => !string.Equals(x.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rewriteCandidates.Count == 0)
        {
            return new DecisionOptionRewriteResult(options, false, "skipped", "no-rewrite-candidates");
        }

        try
        {
            var resolved = await _modelResolutionService.ResolveAsync(
                AppFunction.RolePlayGeneration,
                cancellationToken: cancellationToken);

            var targetActor = ResolveDecisionActorDisplayLabel(session, decisionPoint.TargetActorId);
            var askingActor = ResolveDecisionActorDisplayLabel(session, decisionPoint.AskingActorName);
            var statsSummary = BuildDecisionActorStatSummary(session, decisionPoint.TargetActorId);
            var topThemes = ResolveTopThemeNames(session, 3);
            var activeScenarioId = session.AdaptiveState.ActiveScenarioId;
            var activeTheme = ResolveDecisionThemeContextLabel(session, activeScenarioId);
            var topThemeText = topThemes.Count == 0 ? "(none)" : string.Join(", ", topThemes);

            var systemMessage =
                "You generate context-aware, in-character decision answer options. " +
                "Output ONLY strict JSON with schema: {\"options\":[{\"optionId\":\"id\",\"answer\":\"quoted spoken line\",\"preview\":\"short plain summary\"}]}. " +
                "Never include markdown, explanations, or extra keys.";

            var userMessage = $"""
Scene context:
- Trigger: {decisionPoint.TriggerSource}
- Phase: {decisionPoint.Phase}
- Target actor: {targetActor}
- Asking actor: {askingActor}
- Prompt/context: {decisionPoint.ContextSummary}
- Target actor stats: {statsSummary}
- Active theme/scenario: {activeTheme}
- Top adaptive themes: {topThemeText}

Options to rewrite (keep optionId unchanged):
{JsonSerializer.Serialize(rewriteCandidates.Select(x => new { x.OptionId, baseText = x.DisplayText, basePreview = x.ResponsePreview }))}

Requirements:
1) Keep each option's intent distinct and coherent with the context.
2) answer must be a natural spoken response line in double quotes.
3) preview must be one short non-technical sentence.
4) Do not mention stats, deltas, system prompts, or metadata.
5) Return all provided optionIds exactly once.
6) Keep the response aligned with active scene/theme guidance.
""";

            var modelOutput = await _completionClient.GenerateAsync(systemMessage, userMessage, resolved, cancellationToken);
            var generated = ParseGeneratedDecisionAnswers(modelOutput, rewriteCandidates.Select(x => x.OptionId));
            if (generated.Count == 0)
            {
                return new DecisionOptionRewriteResult(options, false, "fallback", "empty-or-invalid-model-output");
            }

            for (var i = 0; i < options.Count; i++)
            {
                var current = options[i];
                if (!generated.TryGetValue(current.OptionId, out var rewritten))
                {
                    continue;
                }

                options[i] = new DreamGenClone.Domain.RolePlay.DecisionOption
                {
                    OptionId = current.OptionId,
                    DecisionPointId = current.DecisionPointId,
                    DisplayText = rewritten.Answer,
                    ResponsePreview = string.IsNullOrWhiteSpace(rewritten.Preview) ? current.ResponsePreview : rewritten.Preview,
                    BehaviorStyleHint = current.BehaviorStyleHint,
                    CharacterDirectionInstruction = current.CharacterDirectionInstruction,
                    ChatInstruction = current.ChatInstruction,
                    VisibilityMode = current.VisibilityMode,
                    Prerequisites = current.Prerequisites,
                    StatDeltaMap = current.StatDeltaMap,
                    IsCustomResponseFallback = current.IsCustomResponseFallback
                };
            }

            return new DecisionOptionRewriteResult(options, true, "applied", "ai-rewrite-applied");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI decision option rewrite failed for session {SessionId}, decisionPointId={DecisionPointId}", session.Id, decisionPoint.DecisionPointId);
            return new DecisionOptionRewriteResult(options, false, "fallback", "model-call-failed");
        }
    }

    private static string BuildDecisionActorStatSummary(RolePlaySession session, string? actorId)
    {
        if (session.AdaptiveState.CharacterStats.Count == 0)
        {
            return "(no stats)";
        }

        CharacterStatProfileV2? target = null;
        if (!string.IsNullOrWhiteSpace(actorId))
        {
            target = session.AdaptiveState.CharacterStats.Values.FirstOrDefault(x =>
                string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
        }

        target ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (target is null || CharacterStatProfileV2Accessor.GetAllStats(target).Count == 0)
        {
            return "(no stats)";
        }

        return string.Join(", ", AdaptiveStatCatalog.CanonicalStatNames
            .Select(stat => $"{stat}={CharacterStatProfileV2Accessor.GetStatOrDefault(target, stat)}"));
    }

    private static int GetCharacterStatValue(CharacterStatProfileV2 block, string statName)
    {
        return CharacterStatProfileV2Accessor.GetStatOrDefault(block, statName);
    }

    private static Dictionary<string, GeneratedDecisionAnswer> ParseGeneratedDecisionAnswers(
        string modelOutput,
        IEnumerable<string> optionIds)
    {
        var allowed = optionIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(modelOutput))
        {
            return new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);
        }

        if (TryParseGeneratedDecisionAnswersJson(modelOutput, allowed, out var parsed))
        {
            return parsed;
        }

        var firstBrace = modelOutput.IndexOf('{');
        var lastBrace = modelOutput.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var jsonSlice = modelOutput.Substring(firstBrace, lastBrace - firstBrace + 1);
            if (TryParseGeneratedDecisionAnswersJson(jsonSlice, allowed, out parsed))
            {
                return parsed;
            }
        }

        return new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TryParseGeneratedDecisionAnswersJson(
        string json,
        HashSet<string> allowed,
        out Dictionary<string, GeneratedDecisionAnswer> parsed)
    {
        parsed = new Dictionary<string, GeneratedDecisionAnswer>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("options", out var optionsElement)
                || optionsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in optionsElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("optionId", out var optionIdElement)
                    || optionIdElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var optionId = optionIdElement.GetString() ?? string.Empty;
                if (!allowed.Contains(optionId))
                {
                    continue;
                }

                var answer = entry.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.String
                    ? answerElement.GetString() ?? string.Empty
                    : string.Empty;
                var preview = entry.TryGetProperty("preview", out var previewElement) && previewElement.ValueKind == JsonValueKind.String
                    ? previewElement.GetString() ?? string.Empty
                    : string.Empty;

                answer = answer.Trim();
                preview = preview.Trim();
                if (string.IsNullOrWhiteSpace(answer))
                {
                    continue;
                }

                if (!answer.StartsWith('"'))
                {
                    answer = $"\"{answer.Trim('"')}\"";
                }

                if (answer.Length > 180)
                {
                    answer = answer[..180].TrimEnd();
                    if (!answer.EndsWith('"'))
                    {
                        answer += '"';
                    }
                }

                if (preview.Length > 220)
                {
                    preview = preview[..220].TrimEnd();
                }

                parsed[optionId] = new GeneratedDecisionAnswer(answer, preview);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record GeneratedDecisionAnswer(string Answer, string Preview);
    private sealed record DecisionOptionRewriteResult(
        List<DreamGenClone.Domain.RolePlay.DecisionOption> Options,
        bool UsedAiRewrite,
        string Status,
        string Reason);

    private async Task<DreamGenClone.Domain.RolePlay.DecisionOption?> ResolveAppliedDecisionOptionAsync(
        string decisionPointId,
        string optionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(decisionPointId) || string.IsNullOrWhiteSpace(optionId))
        {
            return null;
        }

        var options = await _stateRepository.LoadDecisionOptionsAsync(decisionPointId, cancellationToken);
        return options.FirstOrDefault(x => string.Equals(x.OptionId, optionId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ResolveTopThemeNames(RolePlaySession session, int take)
    {
        return session.AdaptiveState.ThemeScores.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.ThemeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => string.IsNullOrWhiteSpace(x.ThemeName) ? x.ThemeId : x.ThemeName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(Math.Clamp(take, 1, 4))
            .ToList();
    }

    private static (string StatName, int Value) ResolveHighestStat(RolePlaySession session, string? actorId)
    {
        CharacterStatProfileV2? statBlock = null;

        if (!string.IsNullOrWhiteSpace(actorId))
        {
            statBlock = session.AdaptiveState.CharacterStats.Values
                .FirstOrDefault(x => string.Equals(x.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));

            if (statBlock is null && session.AdaptiveState.CharacterStats.TryGetValue(actorId, out var keyedBlock))
            {
                statBlock = keyedBlock;
            }
        }

        statBlock ??= session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (statBlock is null)
        {
            return (string.Empty, 0);
        }

        var allStats = CharacterStatProfileV2Accessor.GetAllStats(statBlock);
        if (allStats.Count == 0)
        {
            return (string.Empty, 0);
        }

        var highest = allStats.OrderByDescending(x => x.Value).First();
        return (highest.Key, highest.Value);
    }

    private static bool IsDeescalatingChoice(IReadOnlyDictionary<string, int> deltas, string highestStatName)
    {
        if (!string.IsNullOrWhiteSpace(highestStatName)
            && deltas.TryGetValue(highestStatName, out var highestDelta)
            && highestDelta < 0)
        {
            return true;
        }

        return (deltas.TryGetValue("Tension", out var tensionDelta) && tensionDelta < 0)
               || (deltas.TryGetValue("Restraint", out var restraintDelta) && restraintDelta > 0);
    }

    private static string ResolveDecisionActorDisplayLabel(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "You";
        }

        var resolved = ResolveLocationActorLabel(session, actorId);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            return actorId;
        }

        var trimmed = resolved.Trim();
        var friendlyMatch = Regex.Match(trimmed, "^(.*)\\s+\\([0-9a-fA-F-]{36}\\)$", RegexOptions.CultureInvariant);
        if (friendlyMatch.Success)
        {
            var friendly = friendlyMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(friendly))
            {
                return friendly;
            }
        }

        return trimmed;
    }

    private static string ResolveDecisionThemeContextLabel(RolePlaySession session, string? scenarioId)
    {
        if (string.IsNullOrWhiteSpace(scenarioId))
        {
            return "(none)";
        }

        if (session.AdaptiveState.ThemeScores.TryGetValue(scenarioId, out var theme)
            && !string.IsNullOrWhiteSpace(theme.ThemeName))
        {
            return $"{theme.ThemeName} ({scenarioId})";
        }

        return scenarioId;
    }

    private static string BuildDecisionResponsePreview(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string targetActorLabel,
        string askingActorLabel,
        IReadOnlyList<string> topThemes)
    {
        if (string.Equals(decisionPoint.TriggerSource, DecisionTrigger.CharacterDirectQuestion.ToString(), StringComparison.OrdinalIgnoreCase)
            && LooksLikeInvitationPrompt(decisionPoint.ContextSummary))
        {
            return optionId switch
            {
                "tempt-answer" => "Enthusiastic acceptance that raises attraction and speeds up chemistry.",
                "lean-in" => "Warm acceptance that keeps things casual while opening connection.",
                "hold-back" => "Polite delay that acknowledges interest without committing right now.",
                "seek-connection" => "Boundary-focused refusal that reinforces loyalty to the relationship.",
                "redirect" => "Neutral refusal that exits the invitation without emotional escalation.",
                "observe" => "Minimal non-commitment while gathering more social context.",
                "custom" => "Write your own in-character answer for this specific question.",
                _ => "Respond in character while preserving social realism and continuity."
            };
        }

        var themeTail = topThemes.Count > 0
            ? $" with a {topThemes[0]} undertone"
            : string.Empty;

        var promptTail = string.Equals(decisionPoint.TriggerSource, "CharacterDirectQuestion", StringComparison.OrdinalIgnoreCase)
            ? $" to {askingActorLabel}"
            : string.Empty;

        return optionId switch
        {
            "lean-in" => $"{targetActorLabel} answers warmly{promptTail} and steps closer{themeTail}.",
            "tempt-answer" => $"{targetActorLabel} gives a daring answer{promptTail}, leaning into forbidden chemistry{themeTail}.",
            "hold-back" => $"{targetActorLabel} answers politely{promptTail}, but sets a calmer boundary{themeTail}.",
            "seek-connection" => $"{targetActorLabel} gives a sincere answer{promptTail} and emphasizes trust{themeTail}.",
            "test-boundary" => $"{targetActorLabel} replies playfully{promptTail}, probing limits without committing{themeTail}.",
            "escalate" => $"{targetActorLabel} responds directly{promptTail}, pushing intensity higher{themeTail}.",
            "redirect" => $"{targetActorLabel} acknowledges the point{promptTail} and redirects toward safer ground{themeTail}.",
            "observe" => $"{targetActorLabel} gives a minimal answer{promptTail} and watches the room{themeTail}.",
            "husband-observes" => $"{targetActorLabel} allows visibility while answering carefully{promptTail}{themeTail}.",
            "custom" => "Write a custom in-character response for this moment.",
            _ => $"{targetActorLabel} responds in character{promptTail}{themeTail}."
        };
    }

    private static string BuildDecisionAnswerChoiceText(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint)
    {
        if (string.Equals(optionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return "Write your own response...";
        }

        var prompt = decisionPoint.ContextSummary;
        var isDirectQuestion = string.Equals(decisionPoint.TriggerSource, DecisionTrigger.CharacterDirectQuestion.ToString(), StringComparison.OrdinalIgnoreCase);

        if (isDirectQuestion && LooksLikeInvitationPrompt(prompt))
        {
            var activity = ResolveInvitationActivity(prompt);
            return optionId switch
            {
                "tempt-answer" => $"\"Definitely, I'd love to {activity}.\"",
                "lean-in" => $"\"Sure, {activity} sounds nice.\"",
                "hold-back" => "\"Maybe in a bit, I have some work to finish first.\"",
                "seek-connection" => "\"I shouldn't, my partner is expecting me.\"",
                "redirect" => "\"Sorry, I'm busy right now.\"",
                "observe" => "\"Maybe another time.\"",
                _ => ResolveDecisionOptionDisplayText(optionId)
            };
        }

        if (isDirectQuestion)
        {
            return optionId switch
            {
                "tempt-answer" => "\"Definitely... yes.\"",
                "lean-in" => "\"Yeah, that sounds good.\"",
                "hold-back" => "\"Not right now, maybe in a bit.\"",
                "seek-connection" => "\"I can't, I need to stay fair to my relationship.\"",
                "redirect" => "\"Let's keep it friendly for now.\"",
                "observe" => "\"Let me think about that.\"",
                _ => ResolveDecisionOptionDisplayText(optionId)
            };
        }

        return ResolveDecisionOptionDisplayText(optionId);
    }

    private static bool LooksLikeInvitationPrompt(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return false;
        }

        return snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("drink", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("lunch", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("grab", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("go with", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("join me", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("with me", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveInvitationActivity(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return "go together";
        }

        if (snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase))
        {
            return "grab coffee together";
        }

        if (snippet.Contains("drink", StringComparison.OrdinalIgnoreCase))
        {
            return "grab a drink";
        }

        if (snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "go to dinner";
        }

        if (snippet.Contains("lunch", StringComparison.OrdinalIgnoreCase))
        {
            return "grab lunch";
        }

        return "go together";
    }

    private static string BuildBehaviorStyleHint(bool deescalating, string highestStatName, int highestStatValue)
    {
        var style = deescalating
            ? "Style: calm, affirming, and de-escalating."
            : "Style: intentional and emotionally clear, but controlled.";

        if (string.IsNullOrWhiteSpace(highestStatName) || highestStatValue < 65)
        {
            return style;
        }

        return $"{style} Keep {highestStatName} stable (current {highestStatValue}).";
    }

    private static string BuildCharacterDirectionInstruction(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        string targetActorLabel,
        string askingActorLabel,
        IReadOnlyList<string> topThemes,
        string highestStatName,
        int highestStatValue,
        bool deescalating)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Character Direction ({targetActorLabel})");
        builder.AppendLine($"Phase: {decisionPoint.Phase}. Trigger: {decisionPoint.TriggerSource}.");

        if (topThemes.Count > 0)
        {
            builder.AppendLine($"Anchor tone to: {string.Join(", ", topThemes)}.");
        }

        if (!string.IsNullOrWhiteSpace(askingActorLabel))
        {
            builder.AppendLine($"Address {askingActorLabel} directly with clear intent.");
        }

        if (!string.IsNullOrWhiteSpace(highestStatName) && highestStatValue >= 65)
        {
            var pressureGuidance = deescalating
                ? "Actively lower pressure and avoid spikes."
                : "Do not spike pressure abruptly; modulate pacing.";
            builder.AppendLine($"High stat signal: {highestStatName}={highestStatValue}. {pressureGuidance}");
        }

        builder.Append(optionId switch
        {
            "lean-in" => "Use receptive language, short affirmations, and consent-forward escalation.",
            "tempt-answer" => "Answer with provocative subtext and attraction-forward language while preserving scene coherence.",
            "hold-back" => "Use respectful boundaries, slower cadence, and emotionally steady wording.",
            "seek-connection" => "Prioritize reassurance, shared goals, and relational clarity.",
            "test-boundary" => "Keep it playful but non-coercive; signal limits before pressure.",
            "escalate" => "Increase intensity through subtext, not blunt force; keep coherence with scene logic.",
            "redirect" => "Acknowledge the request, then steer toward safer and more sustainable momentum.",
            "observe" => "Stay concise, gather cues, and avoid committing to heavy directional moves.",
            "husband-observes" => "Balance transparency and tact; preserve composure under observation.",
            _ => "Respond naturally in character while preserving continuity and scene intent."
        });

        return builder.ToString().Trim();
    }

    private static string BuildChatInstruction(
        string optionId,
        DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint,
        IReadOnlyList<string> topThemes,
        string highestStatName,
        int highestStatValue,
        bool deescalating)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Chat Instruction");
        builder.AppendLine($"For the next 1-2 assistant turns, reflect a {decisionPoint.Phase} cadence and preserve continuity from the chosen decision.");

        if (topThemes.Count > 0)
        {
            builder.AppendLine($"Emphasize themes: {string.Join(", ", topThemes)}.");
        }

        if (!string.IsNullOrWhiteSpace(highestStatName) && highestStatValue >= 65)
        {
            builder.AppendLine(deescalating
                ? $"Bias toward alignment/de-escalation to bring {highestStatName} down from {highestStatValue}."
                : $"Keep {highestStatName} from escalating too sharply from {highestStatValue}.");
        }

        builder.Append(optionId switch
        {
            "custom" => "Honor the user-provided custom response exactly, then continue scene progression naturally.",
            _ => "Carry the selected option's intent forward with coherent emotional follow-through."
        });

        return builder.ToString().Trim();
    }

    private static string BuildDecisionSteeringInstruction(string? selectedDialogue)
    {
        if (!string.IsNullOrWhiteSpace(selectedDialogue))
        {
            return selectedDialogue.Trim();
        }

        return string.Empty;
    }

    private static string BuildDecisionInstructionActorName(RolePlaySession session, string? targetActorId)
    {
        var actorLabel = ResolveLocationActorLabel(session, targetActorId);
        if (string.IsNullOrWhiteSpace(actorLabel)
            || string.Equals(actorLabel, "You", StringComparison.OrdinalIgnoreCase))
        {
            return "Instruction";
        }

        return $"{actorLabel} Instruction";
    }

    private static string? ResolveSelectedDecisionDialogue(
        DreamGenClone.Domain.RolePlay.DecisionOption? selectedOption,
        string? customResponseText)
    {
        if (selectedOption is null)
        {
            return null;
        }

        if (string.Equals(selectedOption.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(customResponseText)
                ? null
                : customResponseText.Trim();
        }

        var display = selectedOption.DisplayText?.Trim();
        if (!string.IsNullOrWhiteSpace(display)
            && !string.Equals(display, "Write your own response...", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(display, "Custom Response", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeDecisionDialogue(display);
        }

        var preview = selectedOption.ResponsePreview?.Trim();
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return NormalizeDecisionDialogue(preview);
        }

        return null;
    }

    private static (string? Dialogue, string Source) ResolveSelectedDecisionDialogueWithSource(
        DreamGenClone.Domain.RolePlay.DecisionOption? selectedOption,
        string? customResponseText)
    {
        if (selectedOption is null)
        {
            return (null, "none");
        }

        if (string.Equals(selectedOption.OptionId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = string.IsNullOrWhiteSpace(customResponseText)
                ? null
                : NormalizeDecisionDialogue(customResponseText);
            return (custom, custom is null ? "custom-empty" : "custom-input");
        }

        var display = selectedOption.DisplayText?.Trim();
        if (!string.IsNullOrWhiteSpace(display)
            && !string.Equals(display, "Write your own response...", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(display, "Custom Response", StringComparison.OrdinalIgnoreCase))
        {
            return (NormalizeDecisionDialogue(display), "display-text");
        }

        var preview = selectedOption.ResponsePreview?.Trim();
        if (!string.IsNullOrWhiteSpace(preview))
        {
            return (NormalizeDecisionDialogue(preview), "response-preview");
        }

        return (null, "none");
    }

    private static string ResolveFallbackDecisionDialogue(string optionId)
    {
        var fallback = ResolveDecisionOptionDisplayText(optionId);
        return NormalizeDecisionDialogue(fallback);
    }

    private static string NormalizeDecisionDialogue(string rawText)
    {
        var trimmed = rawText.Trim();
        if (trimmed.Length == 0)
        {
            return "\"...\"";
        }

        if (trimmed.Length >= 2 && trimmed.StartsWith('"') && trimmed.EndsWith('"'))
        {
            return trimmed;
        }

        return $"\"{trimmed.Trim('"')}\"";
    }

    private static void ApplyDecisionOutcomeToSessionState(RolePlaySession session, DecisionOutcome outcome)
    {
        if (session.AdaptiveState.CharacterStats.Count == 0)
        {
            return;
        }

        if (outcome.PerActorStatDeltas.Count > 0)
        {
            foreach (var (actorId, actorDeltas) in outcome.PerActorStatDeltas)
            {
                var actorEntry = session.AdaptiveState.CharacterStats
                    .FirstOrDefault(x => string.Equals(x.Value.CharacterId, actorId, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(actorEntry.Key))
                {
                    ApplyDeltasToStatBlock(actorEntry.Value, actorDeltas);
                }
            }

            return;
        }

        if (outcome.AppliedStatDeltas.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(outcome.TargetActorId))
        {
            var targetEntry = session.AdaptiveState.CharacterStats
                .FirstOrDefault(x => string.Equals(x.Value.CharacterId, outcome.TargetActorId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(targetEntry.Key))
            {
                ApplyDeltasToStatBlock(targetEntry.Value, outcome.AppliedStatDeltas);
                return;
            }
        }

        var first = session.AdaptiveState.CharacterStats.Values.FirstOrDefault();
        if (first is not null)
        {
            ApplyDeltasToStatBlock(first, outcome.AppliedStatDeltas);
        }
    }

    private static void ApplyDeltasToStatBlock(
        CharacterStatProfileV2 statBlock,
        IReadOnlyDictionary<string, int> deltas)
    {
        var appliedDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (statName, delta) in deltas)
        {
            var current = CharacterStatProfileV2Accessor.GetStatOrDefault(statBlock, statName, AdaptiveStatCatalog.DefaultValue);
            var newValue = Math.Clamp(current + delta, AdaptiveStatCatalog.MinValue, AdaptiveStatCatalog.MaxValue);
            CharacterStatProfileV2Accessor.SetStat(statBlock, statName, newValue);
            if (delta != 0)
            {
                appliedDeltas[statName] = delta;
            }
        }

        if (appliedDeltas.Count > 0)
        {
            statBlock.LastStatDeltas = appliedDeltas;
            statBlock.LastStatDeltaUpdatedUtc = DateTime.UtcNow;
        }

        statBlock.UpdatedUtc = DateTime.UtcNow;
    }

    private static string? ResolveDecisionTargetActorId(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string? askingActorId)
    {
        if (!string.IsNullOrWhiteSpace(askingActorId))
        {
            var asking = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase));
            if (asking is not null)
            {
                return asking.CharacterId;
            }

            var nonAsking = state.CharacterSnapshots.FirstOrDefault(x =>
                !string.Equals(x.CharacterId, askingActorId, StringComparison.OrdinalIgnoreCase));
            if (nonAsking is not null)
            {
                return nonAsking.CharacterId;
            }
        }

        return state.CharacterSnapshots.Count == 1
            ? state.CharacterSnapshots[0].CharacterId
            : null;
    }

    private static string? ResolveDecisionActorId(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        RolePlaySession session,
        string? actorName)
    {
        if (string.IsNullOrWhiteSpace(actorName))
        {
            return null;
        }

        var byId = state.CharacterSnapshots.FirstOrDefault(x =>
            string.Equals(x.CharacterId, actorName, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
        {
            return byId.CharacterId;
        }

        var byPerspectiveName = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterName, actorName, StringComparison.OrdinalIgnoreCase));
        if (byPerspectiveName is not null)
        {
            var perspectiveMatch = state.CharacterSnapshots.FirstOrDefault(x =>
                string.Equals(x.CharacterId, byPerspectiveName.CharacterId, StringComparison.OrdinalIgnoreCase));
            if (perspectiveMatch is not null)
            {
                return perspectiveMatch.CharacterId;
            }
        }

        return null;
    }

    private static IReadOnlyList<DecisionGenerationContext> BuildDecisionGenerationContexts(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        DecisionTrigger trigger,
        DirectQuestionSignal directQuestionSignal,
        string? currentSceneLocation)
    {
        var snippet = session.Interactions
            .TakeLast(4)
            .Select(x => x.Content)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .FirstOrDefault();

        var actorIds = state.CharacterSnapshots
            .Select(x => x.CharacterId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (trigger == DecisionTrigger.CharacterDirectQuestion
            && directQuestionSignal.IsDetected
            && !string.IsNullOrWhiteSpace(directQuestionSignal.TargetActorId))
        {
            return
            [
                new DecisionGenerationContext
                {
                    ScenarioId = session.ScenarioId,
                    TriggerSource = trigger.ToString(),
                    Phase = state.CurrentPhase,
                    Who = InferDecisionWho(directQuestionSignal.PromptSnippet ?? snippet),
                    What = InferDecisionWhat(directQuestionSignal.PromptSnippet ?? snippet),
                    PromptSnippet = directQuestionSignal.PromptSnippet ?? snippet,
                    AskingActorName = directQuestionSignal.AskingActorId,
                    TargetActorId = directQuestionSignal.TargetActorId,
                    IsDirectQuestionContext = true,
                    CurrentSceneLocation = currentSceneLocation,
                    TransparencyOverride = DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit,
                    RelevantActors = state.CharacterSnapshots
                }
            ];
        }

        if (trigger == DecisionTrigger.SceneLocationChanged && actorIds.Count > 0)
        {
            return actorIds.Select(actorId => new DecisionGenerationContext
            {
                ScenarioId = session.ScenarioId,
                TriggerSource = trigger.ToString(),
                Phase = state.CurrentPhase,
                Who = InferDecisionWho(snippet),
                What = InferDecisionWhat(snippet),
                PromptSnippet = snippet,
                AskingActorName = actorId,
                TargetActorId = actorId,
                CurrentSceneLocation = currentSceneLocation,
                TransparencyOverride = DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit,
                RelevantActors = state.CharacterSnapshots
            }).ToList();
        }

        if (actorIds.Count == 0)
        {
            var fallback = ResolveDecisionActorsFromStoryContext(session, state);
            return
            [
                new DecisionGenerationContext
                {
                    ScenarioId = session.ScenarioId,
                    TriggerSource = trigger.ToString(),
                    Phase = state.CurrentPhase,
                    Who = InferDecisionWho(snippet),
                    What = InferDecisionWhat(snippet),
                    PromptSnippet = snippet,
                    AskingActorName = fallback.AskingActorId,
                    TargetActorId = fallback.TargetActorId,
                    CurrentSceneLocation = currentSceneLocation,
                    RelevantActors = state.CharacterSnapshots
                }
            ];
        }

        return actorIds.Select(actorId => new DecisionGenerationContext
        {
            ScenarioId = session.ScenarioId,
            TriggerSource = trigger.ToString(),
            Phase = state.CurrentPhase,
            Who = InferDecisionWho(snippet),
            What = InferDecisionWhat(snippet),
            PromptSnippet = snippet,
            AskingActorName = actorId,
            TargetActorId = actorId,
            CurrentSceneLocation = currentSceneLocation,
            RelevantActors = state.CharacterSnapshots
        }).ToList();
    }

    private DirectQuestionSignal TryDetectDirectQuestionSignal(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var lastInteraction = session.Interactions.LastOrDefault(x =>
            x.InteractionType != InteractionType.System
            && !string.IsNullOrWhiteSpace(x.Content));
        if (lastInteraction is null)
        {
            return DirectQuestionSignal.None;
        }

        var content = lastInteraction.Content.Trim();
        if (!LooksLikeDirectQuestion(content))
        {
            return DirectQuestionSignal.None;
        }

        var askingActorId = ResolveDecisionActorId(state, session, lastInteraction.ActorName);
        if (string.IsNullOrWhiteSpace(askingActorId))
        {
            return DirectQuestionSignal.None;
        }

        var targetActorId = ResolveQuestionTargetActorId(session, state, askingActorId, content)
            ?? ResolveDecisionTargetActorId(state, askingActorId);
        if (string.IsNullOrWhiteSpace(targetActorId))
        {
            return DirectQuestionSignal.None;
        }

        return new DirectQuestionSignal(true, askingActorId, targetActorId, content);
    }

    private async Task<SceneLocationSignal> DetectSceneLocationSignalAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        EnsureCharacterLocationRows(state);

        var scenarioLocationNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
            if (scenario is not null && scenario.Locations.Count > 0)
            {
                scenarioLocationNames = scenario.Locations
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        string? latestLocation = null;
        var previousLocation = state.CurrentSceneLocation;
        var matchedInteraction = default(RolePlayInteraction);

        foreach (var interaction in session.Interactions
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .Reverse())
        {
            var matched = MatchScenarioLocation(interaction.Content, scenarioLocationNames);
            if (string.IsNullOrWhiteSpace(matched))
            {
                matched = MatchGenericLocation(interaction.Content);
            }

            if (string.IsNullOrWhiteSpace(matched))
            {
                continue;
            }

            latestLocation = matched;
            matchedInteraction = interaction;
            break;
        }

        if (string.IsNullOrWhiteSpace(latestLocation))
        {
            var fallbackLocation = previousLocation;

            if (!string.IsNullOrWhiteSpace(fallbackLocation))
            {
                state.CurrentSceneLocation = fallbackLocation;

                foreach (var snapshot in state.CharacterSnapshots)
                {
                    var existing = state.CharacterLocations.FirstOrDefault(x =>
                        string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase));
                    if (existing is null || string.IsNullOrWhiteSpace(existing.TrueLocation))
                    {
                        UpsertTrueLocation(state, snapshot.CharacterId, fallbackLocation, sourceIsHidden: false);
                    }
                }
            }

            UpdatePerceivedLocationsFromTruth(state);
            return new SceneLocationSignal(false, previousLocation, state.CurrentSceneLocation);
        }

        var changed = !string.Equals(previousLocation, latestLocation, StringComparison.OrdinalIgnoreCase);
        state.CurrentSceneLocation = latestLocation;

        var actorId = ResolveDecisionActorId(state, session, matchedInteraction?.ActorName);
        if (matchedInteraction is not null && matchedInteraction.InteractionType == InteractionType.System)
        {
            foreach (var snapshot in state.CharacterSnapshots)
            {
                var existing = state.CharacterLocations.FirstOrDefault(x =>
                    string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (existing is null || string.IsNullOrWhiteSpace(existing.TrueLocation))
                {
                    UpsertTrueLocation(state, snapshot.CharacterId, latestLocation, sourceIsHidden: false);
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(actorId))
        {
            UpsertTrueLocation(state, actorId, latestLocation, sourceIsHidden: false);
        }
        else
        {
            foreach (var snapshot in state.CharacterSnapshots)
            {
                var existing = state.CharacterLocations.FirstOrDefault(x => string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (existing is null || string.IsNullOrWhiteSpace(existing.TrueLocation))
                {
                    UpsertTrueLocation(state, snapshot.CharacterId, latestLocation, sourceIsHidden: false);
                }
            }
        }

        UpdatePerceivedLocationsFromTruth(state);

        if (!changed)
        {
            return new SceneLocationSignal(false, previousLocation, latestLocation);
        }

        return new SceneLocationSignal(true, previousLocation, latestLocation);
    }

    private static void ClearLocationState(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        state.CurrentSceneLocation = null;
        state.CharacterLocations = [];
        state.CharacterLocationPerceptions = [];
    }

    private static void EnsureCharacterLocationRows(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        foreach (var snapshot in state.CharacterSnapshots)
        {
            if (string.IsNullOrWhiteSpace(snapshot.CharacterId))
            {
                continue;
            }

            if (!state.CharacterLocations.Any(x => string.Equals(x.CharacterId, snapshot.CharacterId, StringComparison.OrdinalIgnoreCase)))
            {
                state.CharacterLocations.Add(new DreamGenClone.Domain.RolePlay.CharacterLocationState
                {
                    CharacterId = snapshot.CharacterId,
                    TrueLocation = null,
                    UpdatedUtc = DateTime.UtcNow
                });
            }
        }
    }

    private static void UpsertTrueLocation(
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string characterId,
        string? trueLocation,
        bool sourceIsHidden)
    {
        var row = state.CharacterLocations.FirstOrDefault(x =>
            string.Equals(x.CharacterId, characterId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            row = new DreamGenClone.Domain.RolePlay.CharacterLocationState
            {
                CharacterId = characterId
            };
            state.CharacterLocations.Add(row);
        }

        row.TrueLocation = trueLocation;
        row.IsHidden = sourceIsHidden;
        row.UpdatedUtc = DateTime.UtcNow;
    }

    private static void UpdatePerceivedLocationsFromTruth(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var truthByActor = state.CharacterLocations
            .Where(x => !string.IsNullOrWhiteSpace(x.CharacterId))
            .ToDictionary(x => x.CharacterId, x => x, StringComparer.OrdinalIgnoreCase);
        if (truthByActor.Count == 0)
        {
            return;
        }

        foreach (var observer in truthByActor.Values)
        {
            foreach (var target in truthByActor.Values)
            {
                var row = state.CharacterLocationPerceptions.FirstOrDefault(x =>
                    string.Equals(x.ObserverCharacterId, observer.CharacterId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.TargetCharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase));
                if (row is null)
                {
                    row = new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
                    {
                        ObserverCharacterId = observer.CharacterId,
                        TargetCharacterId = target.CharacterId
                    };
                    state.CharacterLocationPerceptions.Add(row);
                }

                if (string.Equals(observer.CharacterId, target.CharacterId, StringComparison.OrdinalIgnoreCase))
                {
                    row.PerceivedLocation = observer.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "self";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                var sameLocation = !string.IsNullOrWhiteSpace(observer.TrueLocation)
                    && string.Equals(observer.TrueLocation, target.TrueLocation, StringComparison.OrdinalIgnoreCase);
                if (sameLocation && !target.IsHidden)
                {
                    row.PerceivedLocation = target.TrueLocation;
                    row.Confidence = 100;
                    row.HasLineOfSight = true;
                    row.IsInProximity = true;
                    row.KnowledgeSource = "line-of-sight";
                    row.UpdatedUtc = DateTime.UtcNow;
                    continue;
                }

                row.HasLineOfSight = false;
                row.IsInProximity = false;
                if (string.IsNullOrWhiteSpace(row.PerceivedLocation))
                {
                    if (string.IsNullOrWhiteSpace(target.TrueLocation))
                    {
                        row.Confidence = 0;
                        row.KnowledgeSource = "unknown";
                        row.UpdatedUtc = DateTime.UtcNow;
                        continue;
                    }

                    row.PerceivedLocation = target.TrueLocation;
                    row.Confidence = 35;
                    row.KnowledgeSource = "assumed";
                }
                else
                {
                    row.Confidence = Math.Clamp(row.Confidence - 15, 20, 85);
                    row.KnowledgeSource = "last-known";
                }

                row.UpdatedUtc = DateTime.UtcNow;
            }
        }
    }

    private static string? ResolveQuestionTargetActorId(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string askingActorId,
        string content)
    {
        foreach (var perspective in session.CharacterPerspectives)
        {
            if (string.IsNullOrWhiteSpace(perspective.CharacterName))
            {
                continue;
            }

            if (!ContainsWholeWord(content, perspective.CharacterName))
            {
                continue;
            }

            var candidateId = ResolveDecisionActorId(state, session, perspective.CharacterName);
            if (!string.IsNullOrWhiteSpace(candidateId)
                && !string.Equals(candidateId, askingActorId, StringComparison.OrdinalIgnoreCase))
            {
                return candidateId;
            }
        }

        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && ContainsWholeWord(content, "you"))
        {
            var personaId = ResolveDecisionActorId(state, session, session.PersonaName);
            if (!string.IsNullOrWhiteSpace(personaId)
                && !string.Equals(personaId, askingActorId, StringComparison.OrdinalIgnoreCase))
            {
                return personaId;
            }
        }

        return null;
    }

    private static bool LooksLikeDirectQuestion(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        // Require explicit question punctuation to prevent narrative prose from being treated as a direct question.
        return content.Contains('?', StringComparison.Ordinal);
    }

    private static string? MatchScenarioLocation(string content, IEnumerable<string> locationNames)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var ordered = locationNames
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .OrderByDescending(x => x.Length)
            .ToList();
        foreach (var name in ordered)
        {
            if (ContainsWholeWord(content, name))
            {
                return name.Trim();
            }
        }

        return null;
    }

    private static string? MatchGenericLocation(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        foreach (var genericName in GenericLocationNames.OrderByDescending(x => x.Length))
        {
            if (ContainsWholeWord(content, genericName))
            {
                return genericName;
            }
        }

        return null;
    }

    private static bool ContainsWholeWord(string content, string token)
    {
        if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var pattern = $@"\b{Regex.Escape(token.Trim())}\b";
        return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ResolveLocationActorLabel(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "Unknown";
        }

        var token = actorId.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{session.PersonaName} (Persona)";
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase));
        if (perspective is null)
        {
            return token;
        }

        if (!string.IsNullOrWhiteSpace(perspective.CharacterName)
            && string.Equals(perspective.CharacterId, token, StringComparison.OrdinalIgnoreCase))
        {
            return $"{perspective.CharacterName} ({perspective.CharacterId})";
        }

        return string.IsNullOrWhiteSpace(perspective.CharacterName)
            ? token
            : perspective.CharacterName;
    }

    private static string ResolveLocationActorType(RolePlaySession session, string? actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return "unknown";
        }

        var token = actorId.Trim();
        if (!string.IsNullOrWhiteSpace(session.PersonaName)
            && string.Equals(token, session.PersonaName, StringComparison.OrdinalIgnoreCase))
        {
            return "persona";
        }

        return session.CharacterPerspectives.Any(x =>
            string.Equals(x.CharacterId, token, StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.CharacterName, token, StringComparison.OrdinalIgnoreCase))
            ? "character"
            : "unknown";
    }

    private readonly record struct DirectQuestionSignal(
        bool IsDetected,
        string? AskingActorId,
        string? TargetActorId,
        string? PromptSnippet)
    {
        public static DirectQuestionSignal None => new(false, null, null, null);
    }

    private readonly record struct SceneLocationSignal(
        bool Changed,
        string? PreviousLocation,
        string? CurrentLocation)
    {
        public static SceneLocationSignal None => new(false, null, null);
    }

    private static (string? AskingActorId, string? TargetActorId) ResolveDecisionActorsFromStoryContext(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state)
    {
        var recentActorIds = session.Interactions
            .Where(x => x.InteractionType != InteractionType.System)
            .TakeLast(8)
            .Select(x => ResolveDecisionActorId(state, session, x.ActorName))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Reverse()
            .ToList();

        var askingActorId = recentActorIds.FirstOrDefault();
        askingActorId ??= ResolveDecisionActorId(state, session, session.PersonaName);

        var targetActorId = ResolveDecisionTargetActorId(state, askingActorId);

        return (askingActorId, targetActorId);
    }

    private async Task<DreamGenClone.Domain.RolePlay.DecisionPoint?> ResolveDecisionPointAsync(
        string sessionId,
        string decisionPointId,
        CancellationToken cancellationToken)
    {
        var points = await _stateRepository.LoadDecisionPointsAsync(sessionId, 30, cancellationToken);
        return points.FirstOrDefault(x => string.Equals(x.DecisionPointId, decisionPointId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? InferDecisionWho(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        if (snippet.Contains("husband", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("partner", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("spouse", StringComparison.OrdinalIgnoreCase))
        {
            return "husband";
        }

        if (snippet.Contains("coworker", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("colleague", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("boss", StringComparison.OrdinalIgnoreCase))
        {
            return "coworker";
        }

        if (snippet.Contains("friend", StringComparison.OrdinalIgnoreCase))
        {
            return "friend";
        }

        if (snippet.Contains("stranger", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "stranger";
        }

        return null;
    }

    private static string? InferDecisionWhat(string? snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return null;
        }

        if (snippet.Contains("coffee", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("drink", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("dinner", StringComparison.OrdinalIgnoreCase))
        {
            return "invitation";
        }

        if (snippet.Contains("flirt", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("tempt", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("attract", StringComparison.OrdinalIgnoreCase))
        {
            return "temptation";
        }

        if (snippet.Contains("risk", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("public", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("caught", StringComparison.OrdinalIgnoreCase))
        {
            return "risk";
        }

        if (snippet.Contains("trust", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("boundary", StringComparison.OrdinalIgnoreCase)
            || snippet.Contains("relationship", StringComparison.OrdinalIgnoreCase))
        {
            return "boundary";
        }

        return null;
    }

    private static IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> ApplyTransparencyToDecisionOptions(
        IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> options,
        DreamGenClone.Domain.RolePlay.TransparencyMode mode)
    {
        if (mode == DreamGenClone.Domain.RolePlay.TransparencyMode.Explicit)
        {
            return options;
        }

        var transformed = new List<DreamGenClone.Domain.RolePlay.DecisionOption>(options.Count);
        foreach (var option in options)
        {
            var map = ParseDeltaMap(option.StatDeltaMap);

            var transformedMap = mode switch
            {
                DreamGenClone.Domain.RolePlay.TransparencyMode.Hidden => "{}",
                DreamGenClone.Domain.RolePlay.TransparencyMode.Directional => JsonSerializer.Serialize(
                    map.ToDictionary(x => x.Key, x => x.Value >= 0 ? 1 : -1, StringComparer.OrdinalIgnoreCase)),
                _ => option.StatDeltaMap
            };

            transformed.Add(new DreamGenClone.Domain.RolePlay.DecisionOption
            {
                OptionId = option.OptionId,
                DecisionPointId = option.DecisionPointId,
                DisplayText = option.DisplayText,
                ResponsePreview = option.ResponsePreview,
                BehaviorStyleHint = option.BehaviorStyleHint,
                CharacterDirectionInstruction = option.CharacterDirectionInstruction,
                ChatInstruction = option.ChatInstruction,
                VisibilityMode = option.VisibilityMode,
                Prerequisites = option.Prerequisites,
                StatDeltaMap = transformedMap,
                IsCustomResponseFallback = option.IsCustomResponseFallback
            });
        }

        return transformed;
    }

    private static IReadOnlyDictionary<string, int> ParseDeltaMap(string deltaMap)
    {
        if (string.IsNullOrWhiteSpace(deltaMap) || deltaMap == "{}")
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(deltaMap)
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static DreamGenClone.Domain.RolePlay.AdaptiveScenarioState MapToV2State(RolePlaySession session)
    {
        EnsurePersonaCharacterState(session);

        // Prefer name-keyed entries over ID-keyed duplicates when both point to the same CharacterId.
        var snapshots = session.AdaptiveState.CharacterStats
            .OrderBy(x => Guid.TryParse(x.Key, out _) ? 1 : 0)
            .Select(x =>
            {
                var characterId = string.IsNullOrWhiteSpace(x.Value.CharacterId) ? x.Key : x.Value.CharacterId;
                var snapshot = CharacterStatProfileV2Accessor.CreateFromStats(characterId, CharacterStatProfileV2Accessor.GetAllStats(x.Value));
                snapshot.SnapshotUtc = DateTime.UtcNow;
                return snapshot;
            })
            .GroupBy(x => x.CharacterId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return new DreamGenClone.Domain.RolePlay.AdaptiveScenarioState
        {
            SessionId = session.Id,
            ActiveScenarioId = session.AdaptiveState.ActiveScenarioId,
            ActiveVariantId = session.AdaptiveState.ActiveVariantId,
            CurrentPhase = session.AdaptiveState.CurrentPhase,
            InteractionCountInPhase = session.AdaptiveState.InteractionsSinceCommitment,
            ConsecutiveLeadCount = 0,
            LastEvaluationUtc = DateTime.UtcNow,
            CycleIndex = session.AdaptiveState.CompletedScenarios,
            ActiveFormulaVersion = "rpv2-default",
            SelectedWillingnessProfileId = session.AdaptiveState.SelectedWillingnessProfileId,
            SelectedNarrativeGateProfileId = session.AdaptiveState.SelectedNarrativeGateProfileId,
            CharacterEncounterProfileIds = session.AdaptiveState.CharacterEncounterProfileIds
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase),
            PhaseOverrideFloor = session.AdaptiveState.PhaseOverrideFloor,
            PhaseOverrideScenarioId = session.AdaptiveState.PhaseOverrideScenarioId,
            PhaseOverrideCycleIndex = session.AdaptiveState.PhaseOverrideCycleIndex,
            PhaseOverrideSource = session.AdaptiveState.PhaseOverrideSource,
            PhaseOverrideAppliedUtc = session.AdaptiveState.PhaseOverrideAppliedUtc,
            CharacterSnapshots = snapshots,
            CurrentSceneLocation = session.AdaptiveState.CurrentSceneLocation,
            CharacterLocations = session.AdaptiveState.CharacterLocations
                .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationState
                {
                    CharacterId = x.CharacterId,
                    TrueLocation = x.TrueLocation,
                    IsHidden = x.IsHidden,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList(),
            CharacterLocationPerceptions = session.AdaptiveState.CharacterLocationPerceptions
                .Select(x => new DreamGenClone.Domain.RolePlay.CharacterLocationPerceptionState
                {
                    ObserverCharacterId = x.ObserverCharacterId,
                    TargetCharacterId = x.TargetCharacterId,
                    PerceivedLocation = x.PerceivedLocation,
                    Confidence = x.Confidence,
                    HasLineOfSight = x.HasLineOfSight,
                    IsInProximity = x.IsInProximity,
                    KnowledgeSource = x.KnowledgeSource ?? string.Empty,
                    UpdatedUtc = x.UpdatedUtc
                })
                .ToList(),
            ThemeMachineSnapshot = session.AdaptiveState.ThemeMachineSnapshot is null
                ? null
                : CloneThemeMachineSnapshot(session.AdaptiveState.ThemeMachineSnapshot),
            ThemeScores = session.AdaptiveState.ThemeScores
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => new DreamGenClone.Domain.RolePlay.ThemeScoreState
                    {
                        ThemeId = kvp.Value.ThemeId,
                        ThemeName = kvp.Value.ThemeName,
                        Intensity = kvp.Value.Intensity,
                        Score = kvp.Value.Score,
                        Breakdown = new DreamGenClone.Domain.RolePlay.ThemeScoreBreakdownV2
                        {
                            ChoiceSignal = kvp.Value.Breakdown.ChoiceSignal,
                            CharacterStateSignal = kvp.Value.Breakdown.CharacterStateSignal,
                            InteractionEvidenceSignal = kvp.Value.Breakdown.InteractionEvidenceSignal,
                            ScenarioPhaseSignal = kvp.Value.Breakdown.ScenarioPhaseSignal
                        },
                        Blocked = kvp.Value.Blocked,
                        SuppressedHitCount = kvp.Value.SuppressedHitCount,
                        IsScenarioCandidate = kvp.Value.IsScenarioCandidate,
                        NarrativeFitScore = kvp.Value.NarrativeFitScore,
                        LastCandidateEvaluationTimeUtc = kvp.Value.LastCandidateEvaluationTimeUtc,
                        CompletionCooldownInteractions = kvp.Value.CompletionCooldownInteractions,
                        UpdatedUtc = session.AdaptiveState.ThemeTrackerUpdatedUtc
                    },
                    StringComparer.OrdinalIgnoreCase),
            PrimaryThemeId = session.AdaptiveState.PrimaryThemeId,
            SecondaryThemeId = session.AdaptiveState.SecondaryThemeId,
            ThemeSelectionRule = session.AdaptiveState.ThemeSelectionRule,
            ObservedTurnCount = session.AdaptiveState.ObservedTurnCount,
            SelectionMinimumTurns = session.AdaptiveState.SelectionMinimumTurns,
            ThemeTrackerUpdatedUtc = session.AdaptiveState.ThemeTrackerUpdatedUtc,
            RecentEvidence = session.AdaptiveState.RecentEvidence
                .Select(e => new DreamGenClone.Domain.RolePlay.ThemeEvidenceRecord
                {
                    InteractionId = e.InteractionId,
                    ThemeId = e.ThemeId,
                    SignalType = e.SignalType,
                    Delta = e.Delta,
                    Confidence = e.Confidence,
                    Rationale = e.Rationale,
                    CreatedUtc = e.CreatedUtc
                })
                .ToList(),
            SemanticStepSucceeded = session.AdaptiveState.SemanticStepSucceeded,
            SemanticEvents = session.AdaptiveState.SemanticEvents
                .Select(e => new DreamGenClone.Domain.RolePlay.SemanticEventRecord
                {
                    InteractionId = e.InteractionId,
                    EventId = e.EventId,
                    Confidence = e.Confidence,
                    MappingId = e.MappingId,
                    Direction = e.Direction,
                    ThemeTargets = [..e.ThemeTargets],
                    ProcessedUtc = e.ProcessedUtc
                })
                .ToList(),
            SemanticDeltaBreakdowns = session.AdaptiveState.SemanticDeltaBreakdowns
                .Select(d => new DreamGenClone.Domain.RolePlay.SemanticThemeDeltaBreakdown
                {
                    InteractionId = d.InteractionId,
                    ThemeId = d.ThemeId,
                    SourceType = d.SourceType,
                    RawDelta = d.RawDelta,
                    AppliedDelta = d.AppliedDelta,
                    CappedDelta = d.CappedDelta,
                    SuppressedDelta = d.SuppressedDelta,
                    SuppressionReasonCode = d.SuppressionReasonCode
                })
                .ToList(),
            SemanticStatDeltaBreakdowns = session.AdaptiveState.SemanticStatDeltaBreakdowns
                .Select(d => new DreamGenClone.Domain.RolePlay.SemanticStatDeltaRecord
                {
                    InteractionId = d.InteractionId,
                    CharacterId = d.CharacterId,
                    StatName = d.StatName,
                    SourceType = d.SourceType,
                    RawDelta = d.RawDelta,
                    AppliedDelta = d.AppliedDelta,
                    CappedDelta = d.CappedDelta,
                    SuppressedDelta = d.SuppressedDelta,
                    SuppressionReasonCode = d.SuppressionReasonCode,
                    ReasonCode = d.ReasonCode
                })
                .ToList()
        };
    }

    private static DreamGenClone.Domain.RolePlay.ThemeMachineSessionSnapshot CloneThemeMachineSnapshot(
        DreamGenClone.Domain.RolePlay.ThemeMachineSessionSnapshot source)
        => new()
        {
            MachineKey = source.MachineKey,
            ThemeId = source.ThemeId,
            DefinitionId = source.DefinitionId,
            DefinitionVersion = source.DefinitionVersion,
            CurrentStateCode = source.CurrentStateCode,
            TurnsInCurrentState = source.TurnsInCurrentState,
            ReturnBeatCompleted = source.ReturnBeatCompleted,
            LastTransitionId = source.LastTransitionId,
            LastTransitionUtc = source.LastTransitionUtc,
            LastTransitionReasonCode = source.LastTransitionReasonCode,
            LastEvaluatedUtc = source.LastEvaluatedUtc
        };

    private async Task SeedPersonaStatsFromTemplateAsync(RolePlaySession session, CancellationToken cancellationToken)
    {
        if (_templateService is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.PersonaTemplateId)
            || !Guid.TryParse(session.PersonaTemplateId, out var personaTemplateGuid))
        {
            return;
        }

        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();

        // Skip re-seeding if the persona block was already correctly seeded � indicated by
        // a non-empty BaselineStats. BaselineStats is populated only during correct seeding;
        // blocks created via EnsurePersonaCharacterState (when template had no BaseStats at
        // session creation) have empty BaselineStats and will be re-seeded here on each load
        // until a properly-seeded block replaces them.
        if (session.AdaptiveState.CharacterStats.TryGetValue(personaName, out var existingBlock)
            && existingBlock.BaselineStats.Count > 0)
        {
            return;
        }

        var personaTemplate = await _templateService.GetByIdAsync(personaTemplateGuid, cancellationToken);
        if (personaTemplate is null || personaTemplate.BaseStats.Count == 0)
        {
            return;
        }

        var normalizedStats = AdaptiveStatCatalog.NormalizeComplete(personaTemplate.BaseStats);
        var seededPersona = CharacterStatProfileV2Accessor.CreateDefault(personaName);
        CharacterStatProfileV2Accessor.SetAllStats(seededPersona, normalizedStats);
        seededPersona.BaselineStats = new Dictionary<string, int>(normalizedStats, StringComparer.OrdinalIgnoreCase);
        seededPersona.UpdatedUtc = DateTime.UtcNow;
        session.AdaptiveState.CharacterStats[personaName] = seededPersona;

        _logger.LogDebug(
            "Seeded persona '{PersonaName}' stats from template '{TemplateId}' for session {SessionId}",
            personaName, session.PersonaTemplateId, session.Id);
    }

    private static bool EnsurePersonaCharacterState(RolePlaySession session)
    {
        var personaName = string.IsNullOrWhiteSpace(session.PersonaName) ? "You" : session.PersonaName.Trim();
        if (string.IsNullOrWhiteSpace(personaName))
        {
            return false;
        }

        var existing = session.AdaptiveState.CharacterStats.Any(entry =>
            string.Equals(entry.Key, personaName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.Value.CharacterId, personaName, StringComparison.OrdinalIgnoreCase));
        if (existing)
        {
            return false;
        }

        var seedStats = session.AdaptiveState.CharacterStats.Values.FirstOrDefault() is { } seedProfile
            ? CharacterStatProfileV2Accessor.GetAllStats(seedProfile)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var normalizedStats = AdaptiveStatCatalog.NormalizeComplete(seedStats);

        var ensuredProfile = CharacterStatProfileV2Accessor.CreateDefault(personaName);
        CharacterStatProfileV2Accessor.SetAllStats(ensuredProfile, normalizedStats);
        ensuredProfile.UpdatedUtc = DateTime.UtcNow;
        session.AdaptiveState.CharacterStats[personaName] = ensuredProfile;

        return true;
    }

    private async Task<List<ScenarioDefinition>> BuildScenarioCandidatesAsync(RolePlaySession session, AdaptiveScenarioState v2State, CancellationToken cancellationToken)
    {
        var completionCounts = session.AdaptiveState.ScenarioHistory
            .Where(x => !string.IsNullOrWhiteSpace(x.ScenarioId))
            .GroupBy(x => x.ScenarioId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);

        var mostRecentCompletedScenarioId = session.AdaptiveState.ScenarioHistory
            .LastOrDefault(x => !string.IsNullOrWhiteSpace(x.ScenarioId))
            ?.ScenarioId;

        decimal ApplyCompletedScenarioPenalty(string scenarioId, decimal value)
        {
            var normalized = Math.Clamp(value, 0m, 1m);
            if (_completedScenarioRepeatPenaltyPerRun <= 0m
                || completionCounts.Count == 0
                || !completionCounts.TryGetValue(scenarioId, out var completedCount)
                || completedCount <= 0)
            {
                return normalized;
            }

            var multiplier = Math.Max(
                _completedScenarioRepeatPenaltyFloor,
                1m - (_completedScenarioRepeatPenaltyPerRun * completedCount));

            if (!string.IsNullOrWhiteSpace(mostRecentCompletedScenarioId)
                && string.Equals(scenarioId, mostRecentCompletedScenarioId, StringComparison.OrdinalIgnoreCase)
                && _completedScenarioRecentPenaltyMultiplier > 0m
                && _completedScenarioRecentPenaltyMultiplier < 1m)
            {
                multiplier *= _completedScenarioRecentPenaltyMultiplier;
            }

            return Math.Clamp(decimal.Round(normalized * multiplier, 4, MidpointRounding.AwayFromZero), 0m, 1m);
        }

        ScenarioDefinition ApplyRepeatPenalty(ScenarioDefinition candidate)
            => candidate with
            {
                NarrativeEvidenceScore = ApplyCompletedScenarioPenalty(candidate.ScenarioId, candidate.NarrativeEvidenceScore),
                PreferencePriorityScore = ApplyCompletedScenarioPenalty(candidate.ScenarioId, candidate.PreferencePriorityScore)
            };

        var rankedThemes = v2State.ThemeScores.Values
            .Where(theme => !theme.Blocked)
            .Select(theme => new
            {
                Theme = theme,
                // PenalizedScore drives ordering only; uses full composite score.
                PenalizedScore = ApplyCompletedScenarioPenalty(theme.ThemeId, NormalizeThemeScore(theme.Score)),
                // NarrativeEvidenceScore uses only interaction evidence � excludes ChoiceSignal
                // (preference, already in PreferencePriorityScore) and ScenarioPhaseSignal
                // (scenario keyword fit, not user narrative behaviour).
                // ApplyRepeatPenalty will apply the completion penalty when building candidates.
                NarrativeEvidenceScore = NormalizeThemeScore(theme.Breakdown.InteractionEvidenceSignal)
            })
            .OrderByDescending(x => x.PenalizedScore)
            .ThenBy(x => x.Theme.ThemeId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Per-session theme selections: build candidates using tracker score (no repeat penalty)
        // as NarrativeEvidenceScore and tier-based priority as PreferencePriorityScore.
        // Repeat penalties must NOT apply here � the user explicitly chose to play these themes
        // and penalties would prevent second arcs from ever reaching the commit gate.
        if (_rpThemeService is not null && session.SessionThemeSelections.Count > 0)
        {
            var selectionThemes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            var selectionThemesById = selectionThemes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var selectionRoleBindings = await BuildRoleCharacterBindingsAsync(session, cancellationToken);

            var selectionCandidates = session.SessionThemeSelections
                .Where(x => !string.IsNullOrWhiteSpace(x.ThemeId))
                .Select((selection, index) =>
                {
                    if (!selectionThemesById.TryGetValue(selection.ThemeId, out var theme))
                    {
                        return null;
                    }

                    if (v2State.ThemeScores.TryGetValue(selection.ThemeId, out var selectedTrackerItem)
                        && selectedTrackerItem.Blocked)
                    {
                        return null;
                    }

                    var preferencePriority = selection.Tier switch
                    {
                        DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave => 1m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.StronglyPrefer => 0.8m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.NiceToHave => 0.6m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Neutral => 0.5m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Discouraged => 0.2m,
                        _ => 0.5m
                    };

                    // NarrativeEvidenceScore uses only the InteractionEvidenceSignal component of the
                    // tracker breakdown � the organic signal that grows through user interaction choices.
                    // Excludes ChoiceSignal (preference already in PreferencePriorityScore � Bug 1 fix)
                    // and ScenarioPhaseSignal (scenario keyword fit, not narrative buildup � Bug 2 fix).
                    // Fallback is 0 (no evidence yet); PreferencePriorityScore carries tier weight.
                    // v2State.ThemeScores holds the live V2-synced values; session.AdaptiveState may be stale.
                    var trackerScore = selectedTrackerItem is not null
                        ? NormalizeThemeScore(selectedTrackerItem.Breakdown.InteractionEvidenceSignal)
                        : 0m;

                    var fitRulesJson = RPThemeFitRulesConverter.BuildScenarioFitRulesJson(theme, selectionRoleBindings);

                    return new ScenarioDefinition(
                        theme.Id,
                        theme.Label,
                        Priority: Math.Max(1, 5 - index),
                        NarrativeEvidenceScore: trackerScore,
                        PreferencePriorityScore: preferencePriority,
                        ScenarioFitRulesJson: fitRulesJson,
                        ScenarioFitRuleSource: "session-selection",
                        SuccessorCausalityBoost: (decimal)(selectedTrackerItem?.Breakdown.SuccessorCausalityBoost ?? 0),
                        CompletionFitScorePenaltyPoints: (decimal)(selectedTrackerItem?.Breakdown.CompletionFitScorePenalty ?? 0));
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .ToList();

            if (selectionCandidates.Count > 0)
            {
                return selectionCandidates;
            }
        }

        if (_rpThemeService is not null
            && session.SessionThemeSelections.Count == 0
            && !string.IsNullOrWhiteSpace(session.SelectedRPThemeProfileId))
        {
            var assignments = await _rpThemeService.ListProfileAssignmentsAsync(session.SelectedRPThemeProfileId, cancellationToken);
            var themes = await _rpThemeService.ListThemesAsync(includeDisabled: false, cancellationToken: cancellationToken);
            var themesById = themes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
            var roleCharacterBindings = await BuildRoleCharacterBindingsAsync(session, cancellationToken);

            var rpCandidates = assignments
                .Where(x => x.IsEnabled && x.Tier != DreamGenClone.Domain.RolePlay.RPThemeTier.HardDealBreaker)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Tier)
                .Select((assignment, index) =>
                {
                    if (!themesById.TryGetValue(assignment.ThemeId, out var theme))
                    {
                        return null;
                    }

                    if (v2State.ThemeScores.TryGetValue(assignment.ThemeId, out var assignmentTrackerItem)
                        && assignmentTrackerItem.Blocked)
                    {
                        return null;
                    }

                    var preferencePriority = assignment.Tier switch
                    {
                        DreamGenClone.Domain.RolePlay.RPThemeTier.MustHave => 1m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.StronglyPrefer => 0.8m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.NiceToHave => 0.6m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Neutral => 0.5m,
                        DreamGenClone.Domain.RolePlay.RPThemeTier.Discouraged => 0.2m,
                        _ => 0.5m
                    };

                    if (assignment.Weight > 0m)
                    {
                        preferencePriority = Math.Clamp(assignment.Weight, 0m, 1m);
                    }

                    var fitRulesJson = RPThemeFitRulesConverter.BuildScenarioFitRulesJson(theme, roleCharacterBindings);

                    // NarrativeEvidenceScore: pure interaction evidence only (Bug 1 + Bug 2 fix).
                    var trackerScore = assignmentTrackerItem is not null
                        ? NormalizeThemeScore(assignmentTrackerItem.Breakdown.InteractionEvidenceSignal)
                        : 0m;

                    return ApplyRepeatPenalty(new ScenarioDefinition(
                        theme.Id,
                        theme.Label,
                        Priority: Math.Max(1, 5 - index),
                        NarrativeEvidenceScore: trackerScore,
                        PreferencePriorityScore: preferencePriority,
                        ScenarioFitRulesJson: fitRulesJson,
                        ScenarioFitRuleSource: "rp-theme",
                        SuccessorCausalityBoost: (decimal)(assignmentTrackerItem?.Breakdown.SuccessorCausalityBoost ?? 0),
                        CompletionFitScorePenaltyPoints: (decimal)(assignmentTrackerItem?.Breakdown.CompletionFitScorePenalty ?? 0)));
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .Take(5)
                .ToList();

            if (rpCandidates.Count > 0)
            {
                return rpCandidates;
            }
        }

        if (_themePreferenceService is not null && !string.IsNullOrWhiteSpace(session.SelectedThemeProfileId))
        {
            var preferences = await _themePreferenceService.ListByProfileAsync(session.SelectedThemeProfileId, cancellationToken);
            var allowedCatalogIds = preferences
                .Where(x => x.Tier != ThemeTier.HardDealBreaker && !string.IsNullOrWhiteSpace(x.CatalogId))
                .Select(x => x.CatalogId.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (allowedCatalogIds.Count > 0)
            {
                var profileCandidates = rankedThemes
                    .Where(x => allowedCatalogIds.Contains(x.Theme.ThemeId))
                    .Take(5)
                    .Select((theme, index) => ApplyRepeatPenalty(new ScenarioDefinition(
                        theme.Theme.ThemeId,
                        theme.Theme.ThemeName,
                        Priority: 5 - index,
                        NarrativeEvidenceScore: theme.NarrativeEvidenceScore,
                        PreferencePriorityScore: NormalizePreferencePriority(5 - index),
                        SuccessorCausalityBoost: (decimal)theme.Theme.Breakdown.SuccessorCausalityBoost,
                        CompletionFitScorePenaltyPoints: (decimal)theme.Theme.Breakdown.CompletionFitScorePenalty)))
                    .ToList();

                if (profileCandidates.Count > 0)
                {
                    return profileCandidates;
                }
            }
        }

        var candidates = rankedThemes
            .Take(5)
            .Select((theme, index) => ApplyRepeatPenalty(new ScenarioDefinition(
                theme.Theme.ThemeId,
                theme.Theme.ThemeName,
                Priority: 5 - index,
                NarrativeEvidenceScore: theme.NarrativeEvidenceScore,
                PreferencePriorityScore: NormalizePreferencePriority(5 - index),
                SuccessorCausalityBoost: (decimal)theme.Theme.Breakdown.SuccessorCausalityBoost,
                CompletionFitScorePenaltyPoints: (decimal)theme.Theme.Breakdown.CompletionFitScorePenalty)))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates.Add(new ScenarioDefinition(
                session.ScenarioId ?? "default-scenario",
                "Default Scenario",
                Priority: 1,
                NarrativeEvidenceScore: 0.4m,
                PreferencePriorityScore: 0.5m));
        }

        return candidates;
    }

    private async Task<(string? ProfileId, IReadOnlyList<NarrativeGateRule> Rules)> ResolveThemeNarrativeGateConfigAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        if (_rpThemeService is null
            || string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return (null, []);
        }

        var theme = await _rpThemeService.GetThemeAsync(state.ActiveScenarioId, cancellationToken);
        if (theme is null)
        {
            return (null, []);
        }

        if (theme.NarrativeGateRules.Count > 0)
        {
            return (null, theme.NarrativeGateRules);
        }

        if (string.IsNullOrWhiteSpace(theme.NarrativeGateProfileId))
        {
            return (null, []);
        }

        return (theme.NarrativeGateProfileId.Trim(), []);
    }

    private async Task EnsureThemeMachineResolutionGuardAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            state.ThemeMachineSnapshot = null;
            return;
        }

        if (state.ThemeMachineSnapshot is not null
            && !string.Equals(state.ThemeMachineSnapshot.ThemeId, state.ActiveScenarioId, StringComparison.OrdinalIgnoreCase))
        {
            state.ThemeMachineSnapshot = null;
        }

        if (_rpThemeService is null)
        {
            if (state.ThemeMachineSnapshot is null)
            {
                return;
            }

            throw new InvalidOperationException(
                "RP theme service is required to resolve an existing theme machine snapshot.");
        }

        var machineDefinitions = await _rpThemeService.ListMachineDefinitionsAsync(state.ActiveScenarioId, cancellationToken);
        if (machineDefinitions.Count == 0)
        {
            state.ThemeMachineSnapshot = null;
            return;
        }

        if (_themeMachineResolutionService is null)
        {
            throw new InvalidOperationException(
                "Theme machine resolution service is required for role-play machine guard integration.");
        }

        var resolvedDefinition = await _themeMachineResolutionService.ResolveAsync(
            session.Id,
            state.ActiveScenarioId,
            state.ThemeMachineSnapshot,
            cancellationToken);

        if (resolvedDefinition is null)
        {
            state.ThemeMachineSnapshot = null;
            return;
        }

        if (state.ThemeMachineSnapshot is null)
        {
            var initialState = resolvedDefinition.States.SingleOrDefault(x => x.IsInitial)
                ?? throw new InvalidOperationException(
                    $"Theme machine resolution failed for session '{session.Id}': definition '{resolvedDefinition.DefinitionId}' has no initial state.");

            state.ThemeMachineSnapshot = new DreamGenClone.Domain.RolePlay.ThemeMachineSessionSnapshot
            {
                MachineKey = resolvedDefinition.MachineKey,
                ThemeId = resolvedDefinition.ThemeId,
                DefinitionId = resolvedDefinition.DefinitionId,
                DefinitionVersion = resolvedDefinition.Version,
                CurrentStateCode = initialState.StateCode,
                TurnsInCurrentState = 0,
                ReturnBeatCompleted = false,
                LastTransitionId = null,
                LastTransitionUtc = null,
                LastTransitionReasonCode = null,
                LastEvaluatedUtc = DateTime.UtcNow
            };

            await _stateRepository.SaveThemeMachineDiagnosticEventsAsync(
            [
                new DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent
                {
                    SessionId = session.Id,
                    ThemeId = resolvedDefinition.ThemeId,
                    MachineKey = resolvedDefinition.MachineKey,
                    DefinitionVersion = resolvedDefinition.Version,
                    EventType = "init",
                    FromStateCode = null,
                    ToStateCode = initialState.StateCode,
                    TransitionId = null,
                    ReasonCode = "ThemeMachineInitialized",
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        definitionId = resolvedDefinition.DefinitionId,
                        stateCode = initialState.StateCode
                    }),
                    OccurredUtc = DateTime.UtcNow
                }
            ],
            cancellationToken);

            _logger.LogInformation(
                "RolePlayV2 machine initialized: SessionId={SessionId} ThemeId={ThemeId} MachineKey={MachineKey} DefinitionId={DefinitionId} Version={Version} InitialState={InitialState}",
                session.Id,
                resolvedDefinition.ThemeId,
                resolvedDefinition.MachineKey,
                resolvedDefinition.DefinitionId,
                resolvedDefinition.Version,
                initialState.StateCode);

            return;
        }

        if (!resolvedDefinition.States.Any(x =>
                string.Equals(x.StateCode, state.ThemeMachineSnapshot.CurrentStateCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Theme machine resolution failed for session '{session.Id}': snapshot state '{state.ThemeMachineSnapshot.CurrentStateCode}' does not exist in definition '{resolvedDefinition.DefinitionId}'.");
        }
    }

    private static ThemeMachineDirective? BuildDirectiveFromSnapshot(
        string sessionId,
        DreamGenClone.Domain.RolePlay.ThemeMachineSessionSnapshot? snapshot)
    {
        if (snapshot is null || string.IsNullOrWhiteSpace(snapshot.CurrentStateCode))
        {
            return null;
        }

        var requiredNarrativeBeats = new List<string>();
        var promptHardConstraints = new List<string>();
        var reasonCodes = new List<string>();
        var blockDisappearanceCandidates = false;

        if (string.Equals(snapshot.CurrentStateCode, "ReturnBeatRequired", StringComparison.OrdinalIgnoreCase))
        {
            blockDisappearanceCandidates = true;
            requiredNarrativeBeats.Add("ReturnBeatRequired");
            promptHardConstraints.Add("Do not introduce a new disappearance beat until the return beat is completed.");
            reasonCodes.Add("ReturnBeatRequired");
        }
        else if (string.Equals(snapshot.CurrentStateCode, "ReintegrationCooldown", StringComparison.OrdinalIgnoreCase))
        {
            blockDisappearanceCandidates = true;
            requiredNarrativeBeats.Add("ReintegrationCooldown");
            promptHardConstraints.Add("Maintain reintegration continuity until cooldown gates pass.");
            reasonCodes.Add("ReintegrationCooldown");
        }

        return new ThemeMachineDirective
        {
            SessionId = sessionId,
            CurrentStateCode = snapshot.CurrentStateCode,
            BlockDisappearanceCandidates = blockDisappearanceCandidates,
            RequiredNarrativeBeats = requiredNarrativeBeats,
            PromptHardConstraints = promptHardConstraints,
            ReasonCodes = reasonCodes
        };
    }

    private IReadOnlySet<string>? ResolveBlockedScenarioIdsFromDirective(
        string sessionId,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        ThemeMachineDirective? directive,
        IReadOnlyList<ScenarioDefinition> candidates)
    {
        if (directive is null || !directive.BlockDisappearanceCandidates)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            throw new InvalidOperationException(
                $"Theme machine directive enforcement failed for session '{sessionId}': active scenario id is missing while candidate blocking is required.");
        }

        var activeScenarioId = state.ActiveScenarioId.Trim();
        if (!candidates.Any(x => string.Equals(x.ScenarioId, activeScenarioId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Theme machine directive enforcement failed for session '{sessionId}': active scenario '{activeScenarioId}' is not present in the candidate set.");
        }

        var blocked = candidates
            .Where(x => !string.Equals(x.ScenarioId, activeScenarioId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ScenarioId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blocked.Count > 0)
        {
            _logger.LogInformation(
                "RolePlayV2 machine directive blocked non-active candidates: SessionId={SessionId} ActiveScenarioId={ActiveScenarioId} BlockedCount={BlockedCount}",
                sessionId,
                activeScenarioId,
                blocked.Count);
        }

        return blocked;
    }

    private async Task<ThemeMachineDirective?> EvaluateThemeMachineAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId) || state.ThemeMachineSnapshot is null)
        {
            return null;
        }

        if (_themeMachineResolutionService is null)
        {
            throw new InvalidOperationException("Theme machine resolution service is required for role-play machine evaluation.");
        }

        if (_themeMachineEvaluator is null)
        {
            throw new InvalidOperationException("Theme machine evaluator is required for role-play machine evaluation.");
        }

        var resolvedDefinition = await _themeMachineResolutionService.ResolveAsync(
            session.Id,
            state.ActiveScenarioId,
            state.ThemeMachineSnapshot,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Theme machine evaluation failed for session '{session.Id}': no machine definition resolved for active scenario '{state.ActiveScenarioId}'.");

        var evaluation = await _themeMachineEvaluator.EvaluateAsync(
            state,
            new ThemeMachineEvaluationContext
            {
                SessionId = session.Id,
                ActiveScenarioId = state.ActiveScenarioId,
                ThemeId = resolvedDefinition.ThemeId,
                Snapshot = CloneThemeMachineSnapshot(state.ThemeMachineSnapshot),
                Transitions = resolvedDefinition.Transitions,
                GateInputs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["turnsInCurrentState"] = state.ThemeMachineSnapshot.TurnsInCurrentState,
                    ["returnBeatCompleted"] = state.ThemeMachineSnapshot.ReturnBeatCompleted,
                    ["interactionCountInPhase"] = state.InteractionCountInPhase
                }
            },
            cancellationToken);

        state.ThemeMachineSnapshot = CloneThemeMachineSnapshot(evaluation.UpdatedSnapshot);

        if (evaluation.Diagnostics.Count > 0)
        {
            await _stateRepository.SaveThemeMachineDiagnosticEventsAsync(evaluation.Diagnostics, cancellationToken);
        }

        if (evaluation.TransitionApplied)
        {
            _logger.LogInformation(
                "RolePlayV2 machine evaluation applied transition: SessionId={SessionId} ScenarioId={ScenarioId} TransitionId={TransitionId} CurrentState={CurrentState}",
                session.Id,
                state.ActiveScenarioId,
                evaluation.AppliedTransitionId,
                evaluation.UpdatedSnapshot.CurrentStateCode);
        }
        else if (evaluation.Directive.ReasonCodes.Count > 0)
        {
            _logger.LogInformation(
                "RolePlayV2 machine evaluation produced directive reasons: SessionId={SessionId} ScenarioId={ScenarioId} State={State} Reasons={Reasons}",
                session.Id,
                state.ActiveScenarioId,
                evaluation.UpdatedSnapshot.CurrentStateCode,
                string.Join(",", evaluation.Directive.ReasonCodes));
        }

        return evaluation.Directive;
    }

    private async Task PersistThemeMachineFailureDiagnosticAsync(
        RolePlaySession session,
        DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
        string reasonCode,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ActiveScenarioId))
        {
            return;
        }

        await _stateRepository.SaveThemeMachineDiagnosticEventsAsync(
        [
            new ThemeMachineDiagnosticEvent
            {
                SessionId = session.Id,
                ThemeId = state.ActiveScenarioId,
                MachineKey = state.ThemeMachineSnapshot?.MachineKey ?? "unresolved",
                DefinitionVersion = state.ThemeMachineSnapshot?.DefinitionVersion ?? 0,
                EventType = "failure",
                FromStateCode = state.ThemeMachineSnapshot?.CurrentStateCode,
                ToStateCode = state.ThemeMachineSnapshot?.CurrentStateCode,
                TransitionId = null,
                ReasonCode = reasonCode,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    error = message,
                    currentState = state.ThemeMachineSnapshot?.CurrentStateCode,
                    definitionId = state.ThemeMachineSnapshot?.DefinitionId
                }),
                OccurredUtc = DateTime.UtcNow
            }
        ],
        cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildRoleCharacterBindingsAsync(
        RolePlaySession session,
        CancellationToken cancellationToken)
    {
        var bindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(session.PersonaRole)
            && !string.IsNullOrWhiteSpace(session.PersonaName))
        {
            var personaRole = CharacterRoleCatalog.Normalize(session.PersonaRole);
            if (!string.IsNullOrWhiteSpace(personaRole)
                && !string.Equals(personaRole, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
            {
                bindings[personaRole] = session.PersonaName.Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(session.ScenarioId))
        {
            return bindings;
        }

        var scenario = await _scenarioService.GetScenarioAsync(session.ScenarioId);
        if (scenario is null || scenario.Characters.Count == 0)
        {
            return bindings;
        }

        var seenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var character in scenario.Characters)
        {
            var roleName = CharacterRoleCatalog.Normalize(character.Role);
            if (string.IsNullOrWhiteSpace(roleName)
                || string.Equals(roleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase)
                || seenRoles.Contains(roleName)
                || string.IsNullOrWhiteSpace(character.Id))
            {
                continue;
            }

            seenRoles.Add(roleName);
            var boundCharacterId = ResolveFitBindingCharacterId(session, character);
            bindings.TryAdd(roleName, boundCharacterId);
        }

        return bindings;
    }

    private static string ResolveFitBindingCharacterId(RolePlaySession session, DreamGenClone.Web.Domain.Scenarios.Character scenarioCharacter)
    {
        var scenarioCharacterId = (scenarioCharacter.Id ?? string.Empty).Trim();
        var scenarioCharacterName = (scenarioCharacter.Name ?? string.Empty).Trim();

        // Candidate identifiers that may represent this actor in adaptive snapshots.
        var identityCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scenarioCharacterId))
        {
            identityCandidates.Add(scenarioCharacterId);
        }

        if (!string.IsNullOrWhiteSpace(scenarioCharacterName))
        {
            identityCandidates.Add(scenarioCharacterName);
        }

        var perspective = session.CharacterPerspectives.FirstOrDefault(x =>
            string.Equals(x.CharacterId, scenarioCharacterId, StringComparison.OrdinalIgnoreCase));
        var perspectiveName = (perspective?.CharacterName ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(perspectiveName))
        {
            identityCandidates.Add(perspectiveName);
        }

        foreach (var entry in session.AdaptiveState.CharacterStats)
        {
            var key = (entry.Key ?? string.Empty).Trim();
            var blockCharacterId = (entry.Value?.CharacterId ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(blockCharacterId) && identityCandidates.Contains(blockCharacterId))
            {
                return blockCharacterId;
            }

            if (!string.IsNullOrWhiteSpace(key) && identityCandidates.Contains(key))
            {
                return key;
            }
        }

        if (!string.IsNullOrWhiteSpace(perspectiveName))
        {
            return perspectiveName;
        }

        return scenarioCharacterId;
    }

    private static decimal NormalizeThemeScore(double score)
    {
        var normalized = decimal.Round((decimal)score / 100m, 4, MidpointRounding.AwayFromZero);
        return Math.Clamp(normalized, 0m, 1m);
    }

    private static decimal NormalizePreferencePriority(int priority)
    {
        var normalized = decimal.Round(priority / 5m, 4, MidpointRounding.AwayFromZero);
        return Math.Clamp(normalized, 0m, 1m);
    }

    private static BuildUpGateSnapshot ParseBuildUpGateAudit(string? auditMetadataJson)
    {
        if (string.IsNullOrWhiteSpace(auditMetadataJson))
        {
            return BuildUpGateSnapshot.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(auditMetadataJson);
            var root = doc.RootElement;
            return new BuildUpGateSnapshot(
                Passed: ReadNullableBool(root, "passed"),
                Configured: ReadNullableBool(root, "configured") ?? false,
                ProfileId: ReadString(root, "profileId"),
                ProfileName: ReadString(root, "profileName"));
        }
        catch
        {
            return BuildUpGateSnapshot.Empty;
        }
    }

    private static bool? ReadNullableBool(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(root, propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record BuildUpGateSnapshot(bool? Passed, bool Configured, string? ProfileId, string? ProfileName)
    {
        public static BuildUpGateSnapshot Empty { get; } = new(null, false, null, null);
    }

    private static List<DreamGenClone.Domain.RolePlay.BehavioralConcept> BuildConceptCandidates(RolePlaySession session)
    {
        var list = new List<DreamGenClone.Domain.RolePlay.BehavioralConcept>();
        if (!string.IsNullOrWhiteSpace(session.AdaptiveState.PrimaryThemeId))
        {
            list.Add(new DreamGenClone.Domain.RolePlay.BehavioralConcept
            {
                ConceptId = $"theme:{session.AdaptiveState.PrimaryThemeId}",
                Category = "Scenario",
                Priority = 100,
                GuidanceText = "Maintain primary-theme continuity.",
                TriggerConditions = "{}",
                IsEnabled = true
            });
        }

        list.Add(new DreamGenClone.Domain.RolePlay.BehavioralConcept
        {
            ConceptId = "willingness:balance",
            Category = "Willingness",
            Priority = 80,
            GuidanceText = "Balance desire and restraint progression.",
            TriggerConditions = "{}",
            IsEnabled = true
        });

        return list;
    }

    private static DreamGenClone.Domain.RolePlay.NarrativePhase MapPhase(DreamGenClone.Domain.StoryAnalysis.NarrativePhase current)
    {
        return current switch
        {
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp => DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed => DreamGenClone.Domain.RolePlay.NarrativePhase.Committed,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching => DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching,
            DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Climax => DreamGenClone.Domain.RolePlay.NarrativePhase.Climax,
            _ => DreamGenClone.Domain.RolePlay.NarrativePhase.Reset
        };
    }

    private static DreamGenClone.Domain.StoryAnalysis.NarrativePhase MapStoryPhase(DreamGenClone.Domain.RolePlay.NarrativePhase phase)
    {
        return phase switch
        {
            DreamGenClone.Domain.RolePlay.NarrativePhase.BuildUp => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.BuildUp,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Committed => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Committed,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Approaching => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Approaching,
            DreamGenClone.Domain.RolePlay.NarrativePhase.Climax => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Climax,
            _ => DreamGenClone.Domain.StoryAnalysis.NarrativePhase.Reset
        };
    }

    private static InteractionType ToInteractionType(ContinueAsActor actor)
    {
        return actor switch
        {
            ContinueAsActor.You => InteractionType.User,
            ContinueAsActor.Npc => InteractionType.Npc,
            ContinueAsActor.Custom => InteractionType.Custom,
            _ => InteractionType.System
        };
    }

    private static string ResolveActorName(ContinueAsActor actor, string? customActorName)
    {
        return actor switch
        {
            ContinueAsActor.You => "You",
            ContinueAsActor.Npc => "NPC",
            ContinueAsActor.Custom => string.IsNullOrWhiteSpace(customActorName) ? "Custom" : customActorName.Trim(),
            _ => "System"
        };
    }

    private async Task<IReadOnlyList<IdentityOption>> ResolveSelectedIdentityOptionsAsync(
        RolePlaySession session,
        ContinueAsRequest request,
        CancellationToken cancellationToken)
    {
        var identityOptions = await _identityOptionsService.GetIdentityOptionsAsync(session, cancellationToken);
        if (identityOptions.Count == 0)
        {
            return [];
        }

        var selectedById = request.SelectedIdentityIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedById.Count > 0)
        {
            return identityOptions
                .Where(x => x.IsAvailable && selectedById.Contains(x.Id))
                .ToList();
        }

        var selectedActors = ContinueAsOrdering.OrderDistinct(request.SelectedParticipants).ToHashSet();
        if (selectedActors.Count == 0)
        {
            return [];
        }

        return identityOptions
            .Where(x => x.IsAvailable && selectedActors.Contains(x.Actor))
            .ToList();
    }

    private ContinueAsActor ResolveDefaultContinueActor(RolePlaySession session)
    {
        var allowedActors = _behaviorModeService.GetAllowedActors(session.BehaviorMode, explicitSelection: false);
        if (allowedActors.Count == 0)
        {
            return ContinueAsActor.Npc;
        }

        var lastInteraction = session.Interactions.LastOrDefault();
        var preferred = lastInteraction?.InteractionType switch
        {
            InteractionType.User => ContinueAsActor.Npc,
            InteractionType.Custom => ContinueAsActor.Npc,
            InteractionType.Npc => ContinueAsActor.You,
            InteractionType.System => ContinueAsActor.Npc,
            _ => ContinueAsActor.Npc
        };

        if (allowedActors.Contains(preferred))
        {
            return preferred;
        }

        if (allowedActors.Contains(ContinueAsActor.Npc))
        {
            return ContinueAsActor.Npc;
        }

        if (allowedActors.Contains(ContinueAsActor.You))
        {
            return ContinueAsActor.You;
        }

        if (allowedActors.Contains(ContinueAsActor.Custom))
        {
            return ContinueAsActor.Custom;
        }

        return allowedActors.First();
    }

    private static string? ResolveOptionActorName(IdentityOption option, string? customActorName)
    {
        if (option.SourceType == IdentityOptionSource.CustomCharacter)
        {
            return string.IsNullOrWhiteSpace(customActorName) ? option.DisplayName : customActorName.Trim();
        }

        return option.DisplayName;
    }

    private sealed class NullScenarioSelectionService : IScenarioSelectionService
    {
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> EvaluateCandidatesAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            IReadOnlyList<ScenarioDefinition> candidates,
            CancellationToken cancellationToken = default,
            IReadOnlySet<string>? blockedScenarioIds = null)
            => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>>([]);

        public Task<ScenarioCommitResult> TryCommitScenarioAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ScenarioCommitResult { Committed = false, UpdatedConsecutiveLeadCount = 0, Reason = "Selection disabled." });
    }

    private sealed class NullScenarioLifecycleService : IScenarioLifecycleService
    {
        public Task<PhaseTransitionResult> EvaluateTransitionAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            LifecycleInputs inputs,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new PhaseTransitionResult { Transitioned = false, TargetPhase = state.CurrentPhase, Reason = "Lifecycle disabled." });

        public Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState> ExecuteResetAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            ResetReason reason,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>>? perCharacterBaselineOverrides = null,
            IReadOnlyDictionary<string, decimal>? statDecayScaleOverrides = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(state);
    }

    private sealed class NullConceptInjectionService : IConceptInjectionService
    {
        public Task<ConceptInjectionResult> BuildGuidanceAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            ConceptInjectionContext context,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ConceptInjectionResult { SelectedConcepts = [], BudgetCap = context.BudgetCap, BudgetUsed = 0, Rationale = "Concept injection disabled." });
    }

    private sealed class NullDecisionPointService : IDecisionPointService
    {
        public Task<DreamGenClone.Domain.RolePlay.DecisionPoint?> TryCreateDecisionPointAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            DecisionTrigger trigger,
            DecisionGenerationContext? context = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult<DreamGenClone.Domain.RolePlay.DecisionPoint?>(null);

        public Task<DecisionOutcome> ApplyDecisionAsync(
            DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state,
            DecisionSubmission submission,
            string? targetActorId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new DecisionOutcome { Applied = false, DecisionPointId = submission.DecisionPointId, OptionId = submission.OptionId, Summary = "Decision service disabled." });
    }

    private sealed class NullRolePlayStateRepository : IRolePlayStateRepository
    {
        public Task<DreamGenClone.Domain.RolePlay.RolePlayTurn> StartTurnAsync(string sessionId, string turnKind, string triggerSource, string? initiatedByActorName, string? inputInteractionId, CancellationToken cancellationToken = default)
            => Task.FromResult(new DreamGenClone.Domain.RolePlay.RolePlayTurn
            {
                TurnId = Guid.NewGuid().ToString("N"),
                SessionId = sessionId,
                TurnKind = turnKind,
                TriggerSource = triggerSource,
                InitiatedByActorName = initiatedByActorName,
                InputInteractionId = inputInteractionId,
                StartedUtc = DateTime.UtcNow,
                Status = DreamGenClone.Domain.RolePlay.RolePlayTurnStatus.Started
            });
        public Task CompleteTurnAsync(string sessionId, string turnId, IReadOnlyList<string> outputInteractionIds, bool succeeded, string? failureReason = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>> LoadTurnsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.RolePlayTurn>>([]);
        public Task SaveAdaptiveStateAsync(DreamGenClone.Domain.RolePlay.AdaptiveScenarioState state, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?> LoadAdaptiveStateAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<DreamGenClone.Domain.RolePlay.AdaptiveScenarioState?>(null);
        public Task SaveCandidateEvaluationsAsync(IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation> evaluations, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>> LoadCandidateEvaluationsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ScenarioCandidateEvaluation>>([]);
        public Task SaveTransitionEventAsync(DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent transitionEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>> LoadTransitionEventsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.NarrativePhaseTransitionEvent>>([]);
        public Task SaveCompletionMetadataAsync(DreamGenClone.Domain.RolePlay.ScenarioCompletionMetadata metadata, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveDecisionPointAsync(DreamGenClone.Domain.RolePlay.DecisionPoint decisionPoint, IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption> options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>> LoadDecisionPointsAsync(string sessionId, int take = 50, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionPoint>>([]);
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>> LoadDecisionOptionsAsync(string decisionPointId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.DecisionOption>>([]);
        public Task SaveConceptInjectionAsync(string sessionId, ConceptInjectionResult result, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveFormulaVersionReferenceAsync(string sessionId, DreamGenClone.Domain.RolePlay.FormulaConfigVersion version, int cycleIndex, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveUnsupportedSessionErrorAsync(DreamGenClone.Domain.RolePlay.UnsupportedSessionError error, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>> LoadUnsupportedSessionErrorsAsync(string sessionId, int take = 20, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.UnsupportedSessionError>>([]);
        public Task SaveThemeMachineDiagnosticEventsAsync(IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent> events, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent>> LoadThemeMachineDiagnosticEventsAsync(string sessionId, int take = 100, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.ThemeMachineDiagnosticEvent>>([]);
        public Task SaveEncounterSummaryAsync(DreamGenClone.Domain.RolePlay.EncounterSummaryRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateEncounterSummaryLlmAsync(string summaryId, string llmSummary, DateTime llmEnhancedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DreamGenClone.Domain.RolePlay.EncounterSummaryRecord>> LoadEncounterSummariesForSessionAsync(string sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DreamGenClone.Domain.RolePlay.EncounterSummaryRecord>>([]);
    }
}

