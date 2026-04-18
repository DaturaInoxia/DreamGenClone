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
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Application.StoryParser;
using DreamGenClone.Web.Application.Story;
using DreamGenClone.Web.Application.StoryAnalysis;
using DreamGenClone.Application.Processing;
using DreamGenClone.Infrastructure.Processing;
using Microsoft.Extensions.Options;
using DreamGenClone.Application.ModelManager;
using DreamGenClone.Infrastructure.Administration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Web.Application.Administration;
using DreamGenClone.Web.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.RolePlay;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

LoggingSetup.ConfigureSerilog(builder);

builder.Services.Configure<LmStudioOptions>(builder.Configuration.GetSection(LmStudioOptions.SectionName));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.SectionName));
builder.Services.Configure<StoryParserOptions>(builder.Configuration.GetSection(StoryParserOptions.SectionName));
builder.Services.Configure<StoryAnalysisOptions>(builder.Configuration.GetSection(StoryAnalysisOptions.SectionName));
builder.Services.Configure<ScenarioAdaptationOptions>(builder.Configuration.GetSection(ScenarioAdaptationOptions.SectionName));

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
builder.Services.AddScoped<IRolePlayContinuationService, RolePlayContinuationService>();
builder.Services.AddScoped<IRolePlayAdaptiveStateService, RolePlayAdaptiveStateService>();
builder.Services.AddScoped<IRolePlayPromptRouter, RolePlayPromptRouter>();
builder.Services.AddScoped<IRolePlayIdentityOptionsService, RolePlayIdentityOptionsService>();
builder.Services.AddScoped<IBehaviorModeService, BehaviorModeService>();
builder.Services.AddScoped<IRolePlayCommandValidator, RolePlayCommandValidator>();
builder.Services.AddScoped<IRolePlayBranchService, RolePlayBranchService>();
builder.Services.AddScoped<IInteractionCommandService, InteractionCommandService>();
builder.Services.AddScoped<IInteractionRetryService, InteractionRetryService>();
builder.Services.AddScoped<IScenarioSelectionService, ScenarioSelectionService>();
builder.Services.AddScoped<IScenarioLifecycleService, ScenarioLifecycleService>();
builder.Services.AddScoped<ICharacterStateScenarioMapper, CharacterStateScenarioMapper>();
builder.Services.AddScoped<IScenarioGuidanceGenerator, ScenarioGuidanceGenerator>();
builder.Services.AddScoped<ScenarioGuidanceTemplateSeedService>();
builder.Services.AddScoped<IConceptInjectionService, ConceptInjectionService>();
builder.Services.AddScoped<IDecisionPointService, DecisionPointService>();
builder.Services.AddScoped<IOverrideAuthorizationService, OverrideAuthorizationService>();
builder.Services.AddScoped<IRPThemeService, RPThemeService>();
builder.Services.AddScoped<IRolePlayV2StateRepository, RolePlayStateRepository>();
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
builder.Services.AddScoped<INarrativeGateProfileService, NarrativeGateProfileService>();
builder.Services.AddScoped<IHusbandAwarenessProfileService, HusbandAwarenessProfileService>();
builder.Services.AddScoped<IBackgroundCharacterProfileService, BackgroundCharacterProfileService>();
builder.Services.AddScoped<IRoleDefinitionService, RoleDefinitionService>();
builder.Services.AddScoped<IPromptDealbreakerService, PromptDealbreakerService>();
builder.Services.AddScoped<IScenarioFitScoreStrategy, WeightedBlendScenarioFitScoreStrategy>();
builder.Services.AddScoped<IScenarioTieBreakStrategy, TieWindowScenarioTieBreakStrategy>();
builder.Services.AddScoped<IScenarioSelectionEngine, ScenarioSelectionEngine>();
builder.Services.AddScoped<INarrativePhaseManager, NarrativePhaseManager>();
builder.Services.AddScoped<IScenarioGuidanceContextFactory, ScenarioGuidanceContextFactory>();
builder.Services.AddScoped<IStoryRankingService, StoryRankingService>();
builder.Services.AddScoped<StoryAnalysisFacade>();

// Model Manager services
builder.Services.AddSingleton<IProviderRepository, ProviderRepository>();
builder.Services.AddSingleton<IRegisteredModelRepository, RegisteredModelRepository>();
builder.Services.AddSingleton<IFunctionDefaultRepository, FunctionDefaultRepository>();
builder.Services.AddSingleton<IHealthCheckRepository, HealthCheckRepository>();
builder.Services.AddSingleton<IDatabaseBackupRepository, DatabaseBackupRepository>();
builder.Services.AddSingleton<IApiKeyEncryptionService, ApiKeyEncryptionService>();
builder.Services.AddSingleton<ICompletionClient, CompletionClient>();
builder.Services.AddHttpClient("CompletionClient");
builder.Services.AddScoped<IModelResolutionService, ModelResolutionService>();
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

    var statKeywordCategoryService = scope.ServiceProvider.GetRequiredService<IStatKeywordCategoryService>();
    await statKeywordCategoryService.SeedDefaultsAsync();

    var themePreferenceService = scope.ServiceProvider.GetRequiredService<IThemePreferenceService>();
    await themePreferenceService.AutoLinkToCatalogAsync();
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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
