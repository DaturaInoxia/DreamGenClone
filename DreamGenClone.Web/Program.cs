using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.AutoSaveCoordinator;
using CoreAutoSaveCoordinatorContract = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Components;
using DreamGenClone.Application.Administration;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.StoryParser;
using DreamGenClone.Application.Templates;
using DreamGenClone.Application.Validation;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.StoryParser;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Infrastructure.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Web.Application.Assistants;
using DreamGenClone.Web.Application.Export;
using DreamGenClone.Web.Application.Import;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.RolePlay.Prompts;
using DreamGenClone.Web.Application.RolePlay.Prompts.Slots;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Application.StoryParser;
using DreamGenClone.Web.Application.Story;
using DreamGenClone.Web.Application.StoryAnalysis;
using DreamGenClone.Web.Application.BackgroundJobs;
using DreamGenClone.Application.Processing;
using DreamGenClone.Infrastructure.Processing;
using Microsoft.Extensions.Options;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Infrastructure.Administration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Application.PromptTester;
using DreamGenClone.Infrastructure.PromptTester;
using DreamGenClone.Web.Application.Administration;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Infrastructure.RolePlay;
using Microsoft.Extensions.FileProviders;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

// Load git-ignored per-instance secrets (Model Manager API keys, e.g. ModelManagerSecrets:RunPod).
// The file is never committed; without it the app simply runs with DB-stored keys only.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.WebHost.UseStaticWebAssets();

LoggingSetup.ConfigureSerilog(builder);

builder.Services.Configure<LmStudioOptions>(builder.Configuration.GetSection(LmStudioOptions.SectionName));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.SectionName));
builder.Services.Configure<StoryParserOptions>(builder.Configuration.GetSection(StoryParserOptions.SectionName));
builder.Services.Configure<StoryAnalysisOptions>(builder.Configuration.GetSection(StoryAnalysisOptions.SectionName));
builder.Services.Configure<ScenarioAdaptationOptions>(builder.Configuration.GetSection(ScenarioAdaptationOptions.SectionName));
builder.Services.Configure<RolePlayDecisionOptions>(builder.Configuration.GetSection(RolePlayDecisionOptions.SectionName));
builder.Services.Configure<RolePlayFeatureFlagsOptions>(builder.Configuration.GetSection(RolePlayFeatureFlagsOptions.SectionName));
builder.Services.Configure<RolePlayMemoryOptions>(builder.Configuration.GetSection(RolePlayMemoryOptions.SectionName));
builder.Services.Configure<RolePlayPromptOptions>(builder.Configuration.GetSection(RolePlayPromptOptions.SectionName));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<HtmlFetchClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<StoryParserOptions>>().Value;
    httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

builder.Services.AddSingleton<ISqlitePersistence, SqlitePersistence>();
builder.Services.AddSingleton<ITemplateImageStorageService, TemplateImageStorageService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddSingleton<SessionImportValidator>();
builder.Services.AddSingleton<CoreAutoSaveCoordinatorContract, CoreAutoSaveCoordinator>();
builder.Services.AddScoped<IScenarioService, ScenarioService>();
builder.Services.AddScoped<IScenarioAdaptationService, ScenarioAdaptationService>();
builder.Services.AddScoped<IScenarioTokenCounter, ScenarioTokenCounter>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<ISessionCloneForkService, SessionCloneForkService>();
builder.Services.AddScoped<DreamGenClone.Web.Domain.RolePlay.WorkspaceSettingsState>();
builder.Services.AddScoped<DreamGenClone.Web.Application.Sessions.AutoSaveCoordinator>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<ISessionImportService, SessionImportService>();
builder.Services.AddScoped<IStoryEngineService, StoryEngineService>();
builder.Services.AddScoped<IStoryCommandService, StoryCommandService>();
builder.Services.AddSingleton<IAssistantContextManager, AssistantContextManager>();
builder.Services.AddScoped<IWritingAssistantService, WritingAssistantService>();
builder.Services.AddScoped<IRolePlayAssistantService, RolePlayAssistantService>();
builder.Services.AddScoped<IScenarioAssistantService, ScenarioAssistantService>();
builder.Services.AddScoped<RolePlayPromptComposer>();
builder.Services.AddScoped<IRolePlayEngineService, RolePlayEngineService>();
builder.Services.AddScoped<IRolePlayAutoCompleteService, RolePlayAutoCompleteService>();
builder.Services.AddScoped<IRolePlayContinuationService, RolePlayContinuationService>();
builder.Services.AddScoped<IRolePlayAdaptiveStateService, RolePlayAdaptiveStateService>();
builder.Services.AddScoped<ISemanticEventInferenceService, SemanticEventInferenceService>();
builder.Services.AddScoped<ILocationDetectionService, LocationDetectionService>();
builder.Services.AddScoped<IActorSelectionService, ActorSelectionService>();
builder.Services.AddScoped<IRolePlayPromptRouter, RolePlayPromptRouter>();
builder.Services.AddScoped<IRolePlayIdentityOptionsService, RolePlayIdentityOptionsService>();
builder.Services.AddScoped<IBehaviorModeService, BehaviorModeService>();
builder.Services.AddScoped<IRolePlayCommandValidator, RolePlayCommandValidator>();
builder.Services.AddScoped<IRolePlayBranchService, RolePlayBranchService>();
builder.Services.AddScoped<IInteractionCommandService, InteractionCommandService>();
builder.Services.AddScoped<IInteractionRetryService, InteractionRetryService>();
builder.Services.AddScoped<IScenarioSelectionService, ScenarioSelectionService>();
builder.Services.AddScoped<IScenarioEngineSettingsRepository, ScenarioEngineSettingsRepository>();
builder.Services.AddScoped<IScenarioLifecycleService, ScenarioLifecycleService>();
builder.Services.AddScoped<ICharacterStateScenarioMapper, CharacterStateScenarioMapper>();
builder.Services.AddScoped<IScenarioGuidanceGenerator, ScenarioGuidanceGenerator>();
builder.Services.AddScoped<ScenarioGuidanceTemplateSeedService>();
builder.Services.AddScoped<FinishingMoveMatrixSeedService>();
builder.Services.AddScoped<SteerPositionMatrixSeedService>();
    builder.Services.AddScoped<RPPositionSeedService>();
builder.Services.AddScoped<RPFinishLocationSeedService>();
builder.Services.AddScoped<RPFinishFacialTypeSeedService>();
builder.Services.AddScoped<RPFinishReceptivityLevelSeedService>();
builder.Services.AddScoped<RPFinishHisControlLevelSeedService>();
builder.Services.AddScoped<RPFinishTransitionActionSeedService>();
builder.Services.AddScoped<IConceptInjectionService, ConceptInjectionService>();
builder.Services.AddScoped<IDecisionPointService, DecisionPointService>();
builder.Services.AddScoped<IOverrideAuthorizationService, OverrideAuthorizationService>();
builder.Services.AddScoped<IThemeMachineAuthorizationService, ThemeMachineAuthorizationService>();
builder.Services.AddScoped<IThemeMachineResolutionService, ThemeMachineResolutionService>();
builder.Services.AddScoped<IThemeMachineEvaluator, ThemeMachineEvaluator>();
builder.Services.AddScoped<IRPThemeService, RPThemeService>();
builder.Services.AddScoped<IRolePlayStateRepository, RolePlayStateRepository>();

// RP Prompt Redesign (001-rp-prompt-redesign): new prompt architecture
// Phase 1-2: Foundation
builder.Services.AddScoped<IPhaseRuleOfThumbRepository>(sp =>
    new PhaseRuleOfThumbRepository(sp.GetRequiredService<IOptions<PersistenceOptions>>().Value.ConnectionString));
builder.Services.AddScoped<ActorProfileResolver>();
builder.Services.AddScoped<PromptBudgetEnforcer>();
builder.Services.AddScoped<RolePlayPromptBuilder>();

// Phase 3 (US1): Zone A slots + Character Data
builder.Services.AddScoped<IPromptSlot, SystemPrimerSlot>();
builder.Services.AddScoped<IPromptSlot, SceneAnchorSlot>();
builder.Services.AddScoped<IPromptSlot, ActorAssignmentSlot>();
builder.Services.AddScoped<IPromptSlot, TurnContextSlot>();
builder.Services.AddScoped<IPromptSlot, SceneLocationLockSlot>();
builder.Services.AddScoped<IPromptSlot, CharacterDataSlot>();

// Phase 4 (US6): Zone C directive slots (Theme Contract, Behavioral Frames, Final Instruction)
builder.Services.AddScoped<IPromptSlot, ThemeContractSlot>();
builder.Services.AddScoped<IPromptSlot, BehavioralFramesSlot>();
builder.Services.AddScoped<IPromptSlot, FinalInstructionSlot>();

// Phase 6 (US3): Zone B trimmable slots (Scenario Context, Current Location, Writing Style, Scene Continuity Anchor)
builder.Services.AddScoped<IPromptSlot, ScenarioContextSlot>();
builder.Services.AddScoped<IPromptSlot, CurrentLocationSlot>();
builder.Services.AddScoped<IPromptSlot, WritingStyleSlot>();
builder.Services.AddScoped<IPromptSlot, SceneContinuityAnchorSlot>();

// Phase 7 (US4): Zone B tiered-history slots (Interaction History, Session Memory)
builder.Services.AddScoped<IPromptSlot, InteractionHistorySlot>();
builder.Services.AddScoped<IPromptSlot, SessionMemorySlot>();

// Phase 8 (US5): Zone A conditional World State slot (FR-009, B-062)
builder.Services.AddScoped<IPromptSlot, WorldStateSlot>();

// Phase 9 (Polish): Zone C remaining slots (Scenario Guidance, Intensity Pacing, User Direction)
builder.Services.AddScoped<IPromptSlot, PinnedContextSlot>();
builder.Services.AddScoped<IPromptSlot, StagedDirectionsSlot>();
builder.Services.AddScoped<IPromptSlot, ScenarioGuidanceSlot>();
builder.Services.AddScoped<IPromptSlot, IntensityPacingSlot>();
builder.Services.AddScoped<IPromptSlot, UserDirectionSlot>();

// B-082: sticky continuation-settings override slot (Beat Style / Time Shift / Granularity / Scene Presence overrides)
builder.Services.AddScoped<IPromptSlot, ContinuationOverrideSlot>();

builder.Services.AddScoped<IEncounterSummaryService, EncounterSummaryService>();
builder.Services.AddScoped<ISemanticInteractionAnalysisRepository, SemanticInteractionAnalysisRepository>();
builder.Services.AddScoped<IRolePlayDiagnosticsRepository, RolePlayDiagnosticsRepository>();
builder.Services.AddScoped<IRolePlayDiagnosticsService, RolePlayDiagnosticsService>();
builder.Services.AddScoped<RolePlaySessionCompatibilityService>();
builder.Services.AddScoped<RolePlayDebugEventService>();
builder.Services.AddScoped<IRolePlayDebugEventSink>(sp => sp.GetRequiredService<RolePlayDebugEventService>());
builder.Services.AddSingleton<IModelSettingsService, ModelSettingsService>();
builder.Services.AddScoped<IModelRetryService, ModelRetryService>();
builder.Services.AddSingleton<PaginationDiscoveryService>();
builder.Services.AddSingleton<DomainStoryExtractor>();
builder.Services.AddScoped<StoryParserService>();
builder.Services.AddScoped<IStoryParserService>(serviceProvider => serviceProvider.GetRequiredService<StoryParserService>());
builder.Services.AddScoped<IStoryCatalogService>(serviceProvider => serviceProvider.GetRequiredService<StoryParserService>());
builder.Services.AddScoped<StoryParserFacade>();
builder.Services.AddScoped<StoryCatalogFacade>();
builder.Services.AddScoped<IStoryCollectionService, StoryCollectionService>();
builder.Services.AddScoped<ICollectionMatchingService, CollectionMatchingService>();
builder.Services.AddScoped<StoryCollectionFacade>();
builder.Services.AddScoped<IStorySummaryService, StorySummaryService>();
builder.Services.AddScoped<IStoryAnalysisService, StoryAnalysisService>();
builder.Services.AddScoped<IThemeProfileService, ThemeProfileService>();
builder.Services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
builder.Services.AddScoped<IIntensityProfileService, IntensityProfileService>();
builder.Services.AddScoped<ISteeringProfileService, SteeringProfileService>();
builder.Services.AddScoped<IThemeCatalogService, ThemeCatalogService>();
builder.Services.AddScoped<IScenarioDefinitionService, ScenarioDefinitionService>();
builder.Services.AddScoped<IThemeDefinitionParser, ThemeDefinitionParser>();
builder.Services.AddScoped<IThemeDefinitionService, ThemeDefinitionService>();
builder.Services.AddScoped<ICharacterStatPresetImportService, CharacterStatPresetImportService>();
builder.Services.AddScoped<IStatKeywordCategoryService, StatKeywordCategoryService>();
builder.Services.AddScoped<IBaseStatProfileService, BaseStatProfileService>();
builder.Services.AddScoped<IStatWillingnessProfileService, StatWillingnessProfileService>();
builder.Services.AddScoped<IStatResistanceProfileService, StatResistanceProfileService>();
builder.Services.AddScoped<INarrativeGateProfileService, NarrativeGateProfileService>();
builder.Services.AddScoped<IHusbandAwarenessProfileService, HusbandAwarenessProfileService>();
builder.Services.AddScoped<ICharacterProfileService, CharacterProfileService>();
builder.Services.AddScoped<IBehavioralFrameGenerator, CharacterBehavioralFrameGenerator>();
builder.Services.AddScoped<IBackgroundCharacterProfileService, BackgroundCharacterProfileService>();
builder.Services.AddScoped<IRoleDefinitionService, RoleDefinitionService>();
builder.Services.AddScoped<IPromptDealbreakerService, PromptDealbreakerService>();
builder.Services.AddScoped<IScenarioGuidanceContextFactory, ScenarioGuidanceContextFactory>();
builder.Services.AddScoped<IStoryRankingService, StoryRankingService>();
builder.Services.AddScoped<StoryAnalysisFacade>();

// Model Manager services
builder.Services.AddSingleton<IProviderRepository, ProviderRepository>();
builder.Services.AddSingleton<IRegisteredModelRepository, RegisteredModelRepository>();
builder.Services.AddSingleton<IFunctionDefaultRepository, FunctionDefaultRepository>();
builder.Services.AddSingleton<IHealthCheckRepository, HealthCheckRepository>();
builder.Services.AddSingleton<IPromptTestRunRepository, PromptTestRunRepository>();
builder.Services.AddSingleton<IDatabaseBackupRepository, DatabaseBackupRepository>();
builder.Services.AddSingleton<IApiKeyEncryptionService, ApiKeyEncryptionService>();
builder.Services.AddSingleton<ICompletionClient, CompletionClient>();
builder.Services.AddSingleton<IMultimodalCompletionClient, OpenAiMultimodalCompletionClient>();
builder.Services.AddSingleton<IClimaxBeatRepository, ClimaxBeatRepository>();
builder.Services.AddHttpClient("CompletionClient");
builder.Services.AddHttpClient("MultimodalCompletionClient");
builder.Services.AddScoped<IModelResolutionService, ModelResolutionService>();
builder.Services.AddScoped<IMultimodalModelResolutionService>(serviceProvider =>
    serviceProvider.GetRequiredService<IModelResolutionService>() as ModelResolutionService
    ?? throw new InvalidOperationException("The configured model resolver does not support multimodal resolution."));
builder.Services.AddScoped<IImageEditorModelResolver, ImageEditorModelResolver>();
builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
builder.Services.AddScoped<ModelManagerFacade>();
builder.Services.AddScoped<AdministrationFacade>();
builder.Services.AddScoped<ProviderTestService>();
builder.Services.AddScoped<ModelAnalysisService>();
builder.Services.AddScoped<ModelMetadataService>();

// Background model processing queue
builder.Services.AddSingleton<ModelProcessingQueue>();
builder.Services.AddSingleton<IModelProcessingQueue>(sp => sp.GetRequiredService<ModelProcessingQueue>());
builder.Services.AddHostedService<ModelProcessingWorker>();

// Generic background jobs queue
builder.Services.AddSingleton<GenericBackgroundJobQueue>();
builder.Services.AddSingleton<IBackgroundJobQueue>(sp => sp.GetRequiredService<GenericBackgroundJobQueue>());
builder.Services.AddScoped<IBackgroundJobHandler, SemanticInteractionAnalysisJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, EncounterSummaryJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, LocationDetectionJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SteerGenerationJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImageBeatGenerationJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImagePromptGenerationJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImageRenderingJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImageEditingJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImageEditCompilationJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneImageEditDescriptionJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneAssetGenerationJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneAssetEditingJobHandler>();
builder.Services.AddScoped<IBackgroundJobHandler, SceneAssetProfilePackJobHandler>();
builder.Services.AddScoped<SceneImageTurnResolver>();
builder.Services.AddScoped<SceneImageBeatAnalysisService>();
builder.Services.AddHostedService<GenericBackgroundJobWorker>();
builder.Services.AddSingleton<SemanticBackgroundJobQueue>();
builder.Services.AddSingleton<ISemanticBackgroundJobQueue>(sp => sp.GetRequiredService<SemanticBackgroundJobQueue>());
builder.Services.AddHostedService<SemanticBackgroundJobWorker>();

// Scene Image Generator (001-scene-image-generator): image pipeline services.
builder.Services.AddSingleton<IModelManagerSecretProvider, ModelManagerSecretProvider>();
builder.Services.AddSingleton<ImageGenerationClient>();
builder.Services.AddSingleton<ComfyUIImageClient>();
builder.Services.AddSingleton<RunPodServerlessImageClient>();
builder.Services.AddSingleton<IImageGenerationClient, ImageGenerationClientDispatcher>();
builder.Services.AddSingleton<IImageEditingClient, ComfyUIImageEditingClient>();
builder.Services.AddSingleton<ComfyUIIdentityConditionedClient>();
builder.Services.AddSingleton<RunPodServerlessIdentityClient>();
builder.Services.AddSingleton<IIdentityConditionedImageClient, IdentityConditionedImageClientDispatcher>();
builder.Services.AddSingleton<ISceneImageRepository, SceneImageRepository>();
builder.Services.AddSingleton<ISceneImageEditRepository, SceneImageEditRepository>();
builder.Services.AddSingleton<ISceneImageStorageService, SceneImageStorageService>();
builder.Services.AddSingleton<ICharacterImageIdentityRepository, CharacterImageIdentityRepository>();
builder.Services.AddSingleton<ICharacterImageAssetStorageService, CharacterImageAssetStorageService>();
builder.Services.AddScoped<ICharacterImageIdentityService, CharacterImageIdentityService>();
builder.Services.AddSingleton<ISceneAssetRepository, SceneAssetRepository>();
builder.Services.AddSingleton<ISceneAssetStorageService, SceneAssetStorageService>();
builder.Services.AddScoped<ISceneAssetService, SceneAssetService>();
builder.Services.AddScoped<IReferenceImageQualityAnalyzer, ReferenceImageQualityAnalyzer>();
builder.Services.AddScoped<ISceneImageService, SceneImageService>();
builder.Services.AddScoped<ISceneImageEditCompilationService, SceneImageEditCompilationService>();
builder.Services.AddSingleton<PonySceneImagePromptBuilder>();
builder.Services.AddSingleton<IPonySceneImagePromptBuilder>(sp => sp.GetRequiredService<PonySceneImagePromptBuilder>());
builder.Services.AddSingleton<ISceneImageLLMPromptBuilder>(sp => sp.GetRequiredService<PonySceneImagePromptBuilder>());
builder.Services.AddSingleton<ISceneImageEditPromptCompiler, QwenSceneImageEditPromptCompiler>();
builder.Services.AddSingleton<SdxlSceneImagePromptBuilder>();
builder.Services.AddSingleton<ISdxlSceneImagePromptBuilder>(sp => sp.GetRequiredService<SdxlSceneImagePromptBuilder>());

// Prompt-queue navigation resilience (B-027)
builder.Services.AddSingleton<RolePlaySubmissionTracker>();
builder.Services.AddSingleton<IRolePlaySubmissionTracker>(sp => sp.GetRequiredService<RolePlaySubmissionTracker>());

// Increase SignalR message size for large text editing (combined story text)
builder.Services.AddSignalR(o => o.MaximumReceiveMessageSize = 1024 * 1024); // 1 MB

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sqlitePersistence = scope.ServiceProvider.GetRequiredService<ISqlitePersistence>();
    await sqlitePersistence.InitializeAsync();

    var themeCatalogService = scope.ServiceProvider.GetRequiredService<IThemeCatalogService>();
    await themeCatalogService.SeedDefaultsAsync();

    var scenarioGuidanceSeedService = scope.ServiceProvider.GetRequiredService<ScenarioGuidanceTemplateSeedService>();
    await scenarioGuidanceSeedService.SeedDefaultsAsync();

    var finishingMoveMatrixSeedService = scope.ServiceProvider.GetRequiredService<FinishingMoveMatrixSeedService>();
    await finishingMoveMatrixSeedService.SeedDefaultsAsync();

    var rpPositionSeedService = scope.ServiceProvider.GetRequiredService<RPPositionSeedService>();
    await rpPositionSeedService.SeedDefaultsAsync();

    var steerPositionMatrixSeedService = scope.ServiceProvider.GetRequiredService<SteerPositionMatrixSeedService>();
    await steerPositionMatrixSeedService.SeedDefaultsAsync();

    var finishLocationSeedService = scope.ServiceProvider.GetRequiredService<RPFinishLocationSeedService>();
    await finishLocationSeedService.SeedDefaultsAsync();

    var finishFacialTypeSeedService = scope.ServiceProvider.GetRequiredService<RPFinishFacialTypeSeedService>();
    await finishFacialTypeSeedService.SeedDefaultsAsync();

    var finishReceptivitySeedService = scope.ServiceProvider.GetRequiredService<RPFinishReceptivityLevelSeedService>();
    await finishReceptivitySeedService.SeedDefaultsAsync();

    var finishHisControlSeedService = scope.ServiceProvider.GetRequiredService<RPFinishHisControlLevelSeedService>();
    await finishHisControlSeedService.SeedDefaultsAsync();

    var finishTransitionSeedService = scope.ServiceProvider.GetRequiredService<RPFinishTransitionActionSeedService>();
    await finishTransitionSeedService.SeedDefaultsAsync();


    var statKeywordCategoryService = scope.ServiceProvider.GetRequiredService<IStatKeywordCategoryService>();
    await statKeywordCategoryService.SeedDefaultsAsync();

    var climaxBeatRepository = scope.ServiceProvider.GetRequiredService<IClimaxBeatRepository>();
    await climaxBeatRepository.SeedDefaultsAsync();

    var themePreferenceService = scope.ServiceProvider.GetRequiredService<IThemePreferenceService>();
    await themePreferenceService.AutoLinkToCatalogAsync();

    // Seed a default ScenarioEngineSettings row if none exists (fail-fast LoadAsync requires a row).
    var engineSettingsRepository = scope.ServiceProvider.GetRequiredService<IScenarioEngineSettingsRepository>();
    try
    {
        await engineSettingsRepository.LoadAsync();
    }
    catch (InvalidOperationException)
    {
        await engineSettingsRepository.SaveAsync(new DreamGenClone.Domain.RolePlay.ScenarioEngineSettings());
    }

    var characterProfileService = scope.ServiceProvider.GetRequiredService<ICharacterProfileService>();
    await characterProfileService.EnsureDefaultsAsync();
}

// Run startup health checks for all configured providers and models
_ = Task.Run(async () =>
{
    try
    {
        using var healthScope = app.Services.CreateScope();
        var healthCheckService = healthScope.ServiceProvider.GetRequiredService<IHealthCheckService>();
        await healthCheckService.RunAllHealthChecksAsync();
    }
    catch (Exception ex)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogWarning(ex, "Startup health checks failed — results may be stale");
    }
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("CorrelationId", context.TraceIdentifier))
    {
        await next();
    }
});


app.UseAntiforgery();

// Serve generated scene images from the git-ignored scene-image root (kept out of wwwroot).
var sceneImageRoot = app.Services.GetRequiredService<IOptions<PersistenceOptions>>().Value.SceneImageRoot;
var sceneImageFullPath = Path.GetFullPath(sceneImageRoot);
Directory.CreateDirectory(sceneImageFullPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(sceneImageFullPath),
    RequestPath = "/scene-images"
});

app.MapStaticAssets();
app.MapGet("/administration/backups/{backupId}/download", async (string backupId, AdministrationFacade facade, CancellationToken cancellationToken) =>
{
    var download = await facade.GetBackupDownloadAsync(backupId, cancellationToken);
    if (download is null)
    {
        return Results.NotFound();
    }

    return Results.File(download.Value.FilePath, "application/octet-stream", download.Value.Backup.FileName);
});
app.MapGet("/asset-studio/{assetId}/download", async (string assetId, ISceneAssetService assetService, CancellationToken cancellationToken) =>
{
    var (asset, stream) = await assetService.OpenForDownloadAsync(assetId, cancellationToken);
    var name = string.IsNullOrWhiteSpace(asset.Name) ? asset.Id : asset.Name;
    var extension = Path.GetExtension(asset.FileRelativePath ?? string.Empty);
    if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
    return Results.Stream(stream, asset.MediaType, $"{name}{extension}");
});
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
