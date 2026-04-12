namespace DreamGenClone.Web.Application.Scenarios;

using System.Text.Json;
using DreamGenClone.Infrastructure.Persistence;
using DreamGenClone.Web.Domain.RolePlay;
using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementation of scenario orchestration service.
/// Manages scenario CRUD, persistence, and lifecycle operations.
/// </summary>
public class ScenarioService : IScenarioService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<ScenarioService> _logger;
    private readonly Dictionary<string, Scenario> _scenarios = [];
    private bool _isLoaded = false;
    
    public ScenarioService(ISqlitePersistence persistence, ILogger<ScenarioService> logger)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded) return;

        var persisted = await _persistence.LoadAllScenariosAsync();
        foreach (var (id, _, payloadJson, _) in persisted)
        {
            try
            {
                var scenario = JsonSerializer.Deserialize<Scenario>(payloadJson);
                if (scenario != null)
                {
                    _scenarios[id] = scenario;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize scenario {ScenarioId}", id);
            }
        }

        _isLoaded = true;
        _logger.LogInformation("Loaded {ScenarioCount} scenarios from persistence", _scenarios.Count);
    }
    
    public async Task<Scenario> CreateScenarioAsync(string name, string? description = null)
    {
        await EnsureLoadedAsync();

        var scenario = new Scenario
        {
            Name = name,
            Description = description
        };
        
        _scenarios[scenario.Id] = scenario;

        // Persist to database
        var payloadJson = JsonSerializer.Serialize(scenario);
        await _persistence.SaveScenarioAsync(scenario.Id, scenario.Name, payloadJson);
        
        _logger.LogInformation("Scenario created: {ScenarioId} - {ScenarioName}", scenario.Id, name);
        
        return scenario;
    }
    
    public async Task<Scenario?> GetScenarioAsync(string id)
    {
        await EnsureLoadedAsync();

        _scenarios.TryGetValue(id, out var scenario);
        
        if (scenario != null)
        {
            _logger.LogInformation("Scenario retrieved: {ScenarioId}", id);
        }
        
        return scenario;
    }
    
    public async Task<List<Scenario>> GetAllScenariosAsync()
    {
        await EnsureLoadedAsync();

        var scenarios = _scenarios.Values.ToList();
        _logger.LogInformation("Retrieved {ScenarioCount} scenarios", scenarios.Count);
        return scenarios;
    }
    
    public async Task<Scenario> SaveScenarioAsync(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        await EnsureLoadedAsync();
        
        scenario.ModifiedAt = DateTime.UtcNow;
        _scenarios[scenario.Id] = scenario;

        // Persist to database
        var payloadJson = JsonSerializer.Serialize(scenario);
        await _persistence.SaveScenarioAsync(scenario.Id, scenario.Name ?? "Untitled", payloadJson);
        
        _logger.LogInformation("Scenario saved: {ScenarioId} - {ScenarioName}", scenario.Id, scenario.Name);
        
        return scenario;
    }
    
    public async Task<bool> DeleteScenarioAsync(string id)
    {
        await EnsureLoadedAsync();

        var result = _scenarios.Remove(id);

        if (result)
        {
            await _persistence.DeleteScenarioAsync(id);
            _logger.LogInformation("Scenario deleted: {ScenarioId}", id);
        }
        
        return result;
    }
    
    public async Task<Scenario> CloneScenarioAsync(string id, string newName)
    {
        var original = await GetScenarioAsync(id);
        
        if (original == null)
        {
            throw new InvalidOperationException($"Scenario {id} not found");
        }
        
        // Deep copy the scenario
        var clone = new Scenario
        {
            Name = newName,
            Description = original.Description,
            Plot = new Plot
            {
                Title = original.Plot.Title,
                Description = original.Plot.Description,
                Conflicts = new List<string>(original.Plot.Conflicts),
                Goals = new List<string>(original.Plot.Goals)
            },
            Setting = new Setting
            {
                WorldDescription = original.Setting.WorldDescription,
                TimeFrame = original.Setting.TimeFrame,
                EnvironmentalDetails = new List<string>(original.Setting.EnvironmentalDetails),
                WorldRules = new List<string>(original.Setting.WorldRules)
            },
            Narrative = new NarrativeSettings
            {
                NarrativeTone = original.Narrative.NarrativeTone,
                ProseStyle = original.Narrative.ProseStyle,
                PointOfView = original.Narrative.PointOfView,
                NarrativeGuidelines = new List<string>(original.Narrative.NarrativeGuidelines)
            },
            Characters = original.Characters.Select(c => new Character
            {
                Name = c.Name,
                Description = c.Description,
                Role = c.Role,
                TemplateId = c.TemplateId,
                BaseStats = new Dictionary<string, int>(c.BaseStats, StringComparer.OrdinalIgnoreCase),
                PerspectiveMode = c.PerspectiveMode
            }).ToList(),
            Locations = original.Locations.Select(l => new Location
            {
                Name = l.Name,
                Description = l.Description,
                TemplateId = l.TemplateId
            }).ToList(),
            Objects = original.Objects.Select(o => new ScenarioObject
            {
                Name = o.Name,
                Description = o.Description,
                TemplateId = o.TemplateId
            }).ToList(),
            Openings = original.Openings.Select(o => new Opening
            {
                Title = o.Title,
                Text = o.Text
            }).ToList(),
            Examples = original.Examples.Select(e => new Example
            {
                Title = e.Title,
                Text = e.Text
            }).ToList(),
            EstimatedTokenCount = original.EstimatedTokenCount,
            DefaultThemeProfileId = original.DefaultThemeProfileId,
            DefaultIntensityProfileId = original.DefaultIntensityProfileId,
            DefaultSteeringProfileId = original.DefaultSteeringProfileId,
            DefaultIntensityFloor = original.DefaultIntensityFloor,
            DefaultIntensityCeiling = original.DefaultIntensityCeiling,
            BaseStatProfileId = original.BaseStatProfileId,
            ResolvedBaseStats = new Dictionary<string, int>(original.ResolvedBaseStats, StringComparer.OrdinalIgnoreCase),
            DefaultPersonaPerspectiveMode = original.DefaultPersonaPerspectiveMode,
            AssistantChats = original.AssistantChats.Select(chat => new RolePlayAssistantChatThread
            {
                Id = chat.Id,
                Title = chat.Title,
                CreatedAt = chat.CreatedAt,
                ModifiedAt = chat.ModifiedAt,
                Messages = chat.Messages.Select(message => new RolePlayAssistantChatMessage
                {
                    Role = message.Role,
                    Content = message.Content,
                    CreatedAt = message.CreatedAt
                }).ToList()
            }).ToList(),
            ActiveAssistantChatId = original.ActiveAssistantChatId
        };
        
        _scenarios[clone.Id] = clone;

        // Persist to database
        var payloadJson = JsonSerializer.Serialize(clone);
        await _persistence.SaveScenarioAsync(clone.Id, clone.Name ?? "Untitled", payloadJson);
        
        _logger.LogInformation("Scenario cloned: Original={OriginalId}, Clone={CloneId}, CloneName={CloneName}", 
            id, clone.Id, newName);
        
        return clone;
    }
}
