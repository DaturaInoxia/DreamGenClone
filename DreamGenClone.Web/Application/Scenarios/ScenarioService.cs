namespace DreamGenClone.Web.Application.Scenarios;

using DreamGenClone.Web.Domain.Scenarios;
using Microsoft.Extensions.Logging;

/// <summary>
/// Implementation of scenario orchestration service.
/// Manages scenario CRUD, persistence, and lifecycle operations.
/// </summary>
public class ScenarioService : IScenarioService
{
    private readonly ILogger<ScenarioService> _logger;
    private readonly Dictionary<string, Scenario> _scenarios = [];
    
    public ScenarioService(ILogger<ScenarioService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public Task<Scenario> CreateScenarioAsync(string name, string? description = null)
    {
        var scenario = new Scenario
        {
            Name = name,
            Description = description
        };
        
        _scenarios[scenario.Id] = scenario;
        
        _logger.LogInformation("Scenario created: {ScenarioId} - {ScenarioName}", scenario.Id, name);
        
        return Task.FromResult(scenario);
    }
    
    public Task<Scenario?> GetScenarioAsync(string id)
    {
        _scenarios.TryGetValue(id, out var scenario);
        
        if (scenario != null)
        {
            _logger.LogInformation("Scenario retrieved: {ScenarioId}", id);
        }
        
        return Task.FromResult(scenario);
    }
    
    public Task<List<Scenario>> GetAllScenariosAsync()
    {
        var scenarios = _scenarios.Values.ToList();
        _logger.LogInformation("Retrieved {ScenarioCount} scenarios", scenarios.Count);
        return Task.FromResult(scenarios);
    }
    
    public Task<Scenario> SaveScenarioAsync(Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        
        scenario.ModifiedAt = DateTime.UtcNow;
        _scenarios[scenario.Id] = scenario;
        
        _logger.LogInformation("Scenario saved: {ScenarioId} - {ScenarioName}", scenario.Id, scenario.Name);
        
        return Task.FromResult(scenario);
    }
    
    public Task<bool> DeleteScenarioAsync(string id)
    {
        var result = _scenarios.Remove(id);
        
        if (result)
        {
            _logger.LogInformation("Scenario deleted: {ScenarioId}", id);
        }
        
        return Task.FromResult(result);
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
            Style = new Style
            {
                Tone = original.Style.Tone,
                WritingStyle = original.Style.WritingStyle,
                PointOfView = original.Style.PointOfView,
                StyleGuidelines = new List<string>(original.Style.StyleGuidelines)
            },
            Characters = original.Characters.Select(c => new Character
            {
                Name = c.Name,
                Description = c.Description,
                Role = c.Role,
                TemplateId = c.TemplateId
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
            EstimatedTokenCount = original.EstimatedTokenCount
        };
        
        _scenarios[clone.Id] = clone;
        
        _logger.LogInformation("Scenario cloned: Original={OriginalId}, Clone={CloneId}, CloneName={CloneName}", 
            id, clone.Id, newName);
        
        return clone;
    }
}
