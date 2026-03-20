using DreamGenClone.Web.Domain.Models;

namespace DreamGenClone.Web.Application.Models;

/// <summary>
/// Manages per-session model settings for LM Studio generation.
/// </summary>
public interface IModelSettingsService
{
    /// <summary>
    /// Gets the current model settings for a session. Returns default settings if not yet configured.
    /// </summary>
    ModelSettings GetSettings(string sessionId);

    /// <summary>
    /// Updates the model settings for a session.
    /// </summary>
    void UpdateSettings(string sessionId, ModelSettings settings);

    /// <summary>
    /// Clears settings for a specific session (used on session deletion).
    /// </summary>
    void ClearSettings(string sessionId);
}
