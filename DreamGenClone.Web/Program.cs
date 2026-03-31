using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.AutoSaveCoordinator;
using CoreAutoSaveCoordinatorContract = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Components;
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
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

LoggingSetup.ConfigureSerilog(builder);

builder.Services.Configure<LmStudioOptions>(builder.Configuration.GetSection(LmStudioOptions.SectionName));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.SectionName));
builder.Services.Configure<StoryParserOptions>(builder.Configuration.GetSection(StoryParserOptions.SectionName));
builder.Services.Configure<StoryAnalysisOptions>(builder.Configuration.GetSection(StoryAnalysisOptions.SectionName));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<ILmStudioClient, LmStudioClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LmStudioOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
    httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

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
builder.Services.AddScoped<IRolePlayEngineService, RolePlayEngineService>();
builder.Services.AddScoped<IRolePlayContinuationService, RolePlayContinuationService>();
builder.Services.AddScoped<IRolePlayPromptRouter, RolePlayPromptRouter>();
builder.Services.AddScoped<IRolePlayIdentityOptionsService, RolePlayIdentityOptionsService>();
builder.Services.AddScoped<IBehaviorModeService, BehaviorModeService>();
builder.Services.AddScoped<IRolePlayCommandValidator, RolePlayCommandValidator>();
builder.Services.AddScoped<IRolePlayBranchService, RolePlayBranchService>();
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
builder.Services.AddScoped<IRankingProfileService, RankingProfileService>();
builder.Services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
builder.Services.AddScoped<IStoryRankingService, StoryRankingService>();
builder.Services.AddScoped<StoryAnalysisFacade>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var sqlitePersistence = scope.ServiceProvider.GetRequiredService<ISqlitePersistence>();
    await sqlitePersistence.InitializeAsync();
}

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


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
