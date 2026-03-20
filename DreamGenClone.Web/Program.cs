using CoreAutoSaveCoordinator = DreamGenClone.Application.Sessions.AutoSaveCoordinator;
using CoreAutoSaveCoordinatorContract = DreamGenClone.Application.Sessions.IAutoSaveCoordinator;
using DreamGenClone.Components;
using DreamGenClone.Application.Abstractions;
using DreamGenClone.Application.Templates;
using DreamGenClone.Application.Validation;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.Logging;
using DreamGenClone.Infrastructure.Models;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Infrastructure.Storage;
using DreamGenClone.Web.Application.Assistants;
using DreamGenClone.Web.Application.Export;
using DreamGenClone.Web.Application.Import;
using DreamGenClone.Web.Application.Models;
using DreamGenClone.Web.Application.RolePlay;
using DreamGenClone.Web.Application.Scenarios;
using DreamGenClone.Web.Application.Sessions;
using DreamGenClone.Web.Application.Story;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseStaticWebAssets();

LoggingSetup.ConfigureSerilog(builder);

builder.Services.Configure<LmStudioOptions>(builder.Configuration.GetSection(LmStudioOptions.SectionName));
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection(PersistenceOptions.SectionName));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<ILmStudioClient, LmStudioClient>((serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<LmStudioOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.BaseUrl);
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
builder.Services.AddScoped<IRolePlayBranchService, RolePlayBranchService>();
builder.Services.AddSingleton<IModelSettingsService, ModelSettingsService>();
builder.Services.AddScoped<IModelRetryService, ModelRetryService>();

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
