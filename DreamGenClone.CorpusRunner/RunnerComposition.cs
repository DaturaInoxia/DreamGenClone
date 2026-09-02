using DreamGenClone.Application.ModelManager;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using DreamGenClone.Infrastructure.ModelManager;
using DreamGenClone.Infrastructure.RolePlay;
using DreamGenClone.Web.Application.RolePlay;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DreamGenClone.CorpusRunner;

public sealed record RunnerRepositoryConnections(string ConfigurationConnectionString, string StageConnectionString)
{
    public static RunnerRepositoryConnections FromPlan(RunnerDatabasePlan plan)
    {
        var live = $"Data Source={Path.GetFullPath(plan.ConfigurationDatabasePath)};Mode=ReadOnly";
        var stage = $"Data Source={Path.GetFullPath(plan.WorkingDatabasePath)}";
        if (string.Equals(Path.GetFullPath(plan.ConfigurationDatabasePath), Path.GetFullPath(plan.WorkingDatabasePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live configuration and stage repository databases must be separate.");
        return new RunnerRepositoryConnections(live, stage);
    }
}

internal sealed class RunnerComposition
{
    public RunnerComposition(RunnerDatabasePlan databasePlan)
    {
        Connections = RunnerRepositoryConnections.FromPlan(databasePlan);
        var liveOptions = Options.Create(new PersistenceOptions { ConnectionString = Connections.ConfigurationConnectionString });
        var stageOptions = Options.Create(new PersistenceOptions { ConnectionString = Connections.StageConnectionString });

        FunctionDefaults = new FunctionDefaultRepository(liveOptions, NullLogger<FunctionDefaultRepository>.Instance);
        Models = new RegisteredModelRepository(liveOptions, NullLogger<RegisteredModelRepository>.Instance);
        Providers = new ProviderRepository(liveOptions, NullLogger<ProviderRepository>.Instance);
        AnalyzerResolver = new SceneBeatAnalyzerResolver(FunctionDefaults, Models, Providers);

        Catalogues = new SceneBeatCatalogueRepository(stageOptions);
        ProductionPlans = new SceneBeatProductionPlanRepository(stageOptions);
        MomentSets = new SceneMomentSetRepository(stageOptions);
        Enrichments = new SceneMomentEnrichmentRepository(stageOptions);
    }

    public RunnerRepositoryConnections Connections { get; }
    public IFunctionDefaultRepository FunctionDefaults { get; }
    public IRegisteredModelRepository Models { get; }
    public IProviderRepository Providers { get; }
    public ISceneBeatAnalyzerResolver AnalyzerResolver { get; }
    public ISceneBeatCatalogueRepository Catalogues { get; }
    public ISceneBeatProductionPlanRepository ProductionPlans { get; }
    public ISceneMomentSetRepository MomentSets { get; }
    public ISceneMomentEnrichmentRepository Enrichments { get; }
}