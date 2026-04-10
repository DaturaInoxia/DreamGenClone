namespace DreamGenClone.Web.Domain.Scenarios;

/// <summary>
/// Represents the literary/narrative style component of a scenario.
/// Includes tone, writing style preferences, and narrative constraints.
/// </summary>
public class Style
{
    public string? Tone { get; set; }
    public string? WritingStyle { get; set; }
    public string? PointOfView { get; set; }
    public List<string> StyleGuidelines { get; set; } = [];

    /// <summary>
    /// Optional reference to a reusable tone profile.
    /// </summary>
    public string? ToneProfileId { get; set; }

    /// <summary>
    /// Optional reference to a reusable style profile.
    /// </summary>
    public string? StyleProfileId { get; set; }

    /// <summary>
    /// Optional lower style boundary for this scenario.
    /// </summary>
    public string? StyleFloor { get; set; }

    /// <summary>
    /// Optional upper style boundary for this scenario.
    /// </summary>
    public string? StyleCeiling { get; set; }
}
