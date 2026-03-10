namespace DreamGenClone.Web.Application.Scenarios;

using DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Service for managing scenario lifecycle operations.
/// Provides CRUD, search, and template integration for scenarios.
/// </summary>
public interface IScenarioService
{
    /// <summary>
    /// Create a new scenario.
    /// </summary>
    Task<Scenario> CreateScenarioAsync(string name, string? description = null);
    
    /// <summary>
    /// Get a scenario by ID.
    /// </summary>
    Task<Scenario?> GetScenarioAsync(string id);
    
    /// <summary>
    /// Get all scenarios.
    /// </summary>
    Task<List<Scenario>> GetAllScenariosAsync();
    
    /// <summary>
    /// Update/save a scenario.
    /// Updates ModifiedAt timestamp.
    /// </summary>
    Task<Scenario> SaveScenarioAsync(Scenario scenario);
    
    /// <summary>
    /// Delete a scenario by ID.
    /// </summary>
    Task<bool> DeleteScenarioAsync(string id);
    
    /// <summary>
    /// Clone a scenario, creating an independent copy.
    /// </summary>
    Task<Scenario> CloneScenarioAsync(string id, string newName);
}
