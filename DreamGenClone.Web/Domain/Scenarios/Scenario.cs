namespace DreamGenClone.Web.Domain.Scenarios;

using System.Text.Json.Serialization;
using DreamGenClone.Web.Domain.RolePlay;

/// <summary>
/// Represents a complete scenario definition.
/// Scenarios contain plot, setting, narrative defaults, engine defaults, characters, locations, objects, openings, and examples.
/// </summary>
public class Scenario
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? Name { get; set; }
    public string? Description { get; set; }
    
    /// <summary>
    /// Plot component including conflicts and goals.
    /// </summary>
    public Plot Plot { get; set; } = new();
    
    /// <summary>
    /// Setting/world component.
    /// </summary>
    public Setting Setting { get; set; } = new();
    
    /// <summary>
    /// Prose-focused narrative defaults.
    /// </summary>
    public NarrativeSettings Narrative { get; set; } = new();

    /// <summary>Legacy alias — maps old Style object shape during deserialization.</summary>
    [JsonInclude]
    [JsonPropertyName("Style")]
    public LegacyScenarioStyle? LegacyStyle
    {
        get => null;
        set
        {
            if (value is null)
            {
                return;
            }

            Narrative = new NarrativeSettings
            {
                NarrativeTone = value.NarrativeTone,
                ProseStyle = value.ProseStyle,
                PointOfView = value.PointOfView,
                NarrativeGuidelines = value.NarrativeGuidelines.Count > 0
                    ? new List<string>(value.NarrativeGuidelines)
                    : []
            };

            DefaultIntensityProfileId ??= value.IntensityProfileId;
            DefaultSteeringProfileId ??= value.SteeringProfileId;
            DefaultIntensityFloor ??= value.IntensityFloor;
            DefaultIntensityCeiling ??= value.IntensityCeiling;
        }
    }
    
    /// <summary>
    /// Characters available in this scenario.
    /// </summary>
    public List<Character> Characters { get; set; } = [];
    
    /// <summary>
    /// Locations available in this scenario.
    /// </summary>
    public List<Location> Locations { get; set; } = [];
    
    /// <summary>
    /// Objects/items available in this scenario.
    /// </summary>
    public List<ScenarioObject> Objects { get; set; } = [];
    
    /// <summary>
    /// Possible scenario openings/starts.
    /// </summary>
    public List<Opening> Openings { get; set; } = [];
    
    /// <summary>
    /// Example scenarios or interactions.
    /// </summary>
    public List<Example> Examples { get; set; } = [];
    
    /// <summary>
    /// Estimated token count for this scenario.
    /// Updated when scenario content changes.
    /// </summary>
    public int EstimatedTokenCount { get; set; }
    
    /// <summary>
    /// Creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Last modification timestamp in UTC.
    /// </summary>
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The ID of the parsed story this scenario was adapted from, if any.
    /// </summary>
    public string? SourceParsedStoryId { get; set; }

    /// <summary>
    /// Default theme profile to use when creating sessions from this scenario.
    /// </summary>
    [JsonPropertyName("DefaultThemeProfileId")]
    public string? DefaultThemeProfileId { get; set; }

    /// <summary>
    /// Default RP theme profile to use for RolePlay sessions and runtime scenario guidance.
    /// </summary>
    public string? DefaultRPThemeProfileId { get; set; }

    /// <summary>Legacy alias — maps old DefaultRankingProfileId during deserialization.</summary>
    [JsonInclude]
    [JsonPropertyName("DefaultRankingProfileId")]
    public string? LegacyDefaultRankingProfileId
    {
        get => null;
        set { if (value is not null && DefaultThemeProfileId is null) DefaultThemeProfileId = value; }
    }

    /// <summary>
    /// Default intensity profile to use when creating sessions from this scenario.
    /// </summary>
    public string? DefaultIntensityProfileId { get; set; }

    /// <summary>
    /// Default steering profile to use when creating sessions from this scenario.
    /// </summary>
    public string? DefaultSteeringProfileId { get; set; }

    /// <summary>
    /// Optional lower intensity boundary for this scenario.
    /// </summary>
    public string? DefaultIntensityFloor { get; set; }

    /// <summary>
    /// Optional upper intensity boundary for this scenario.
    /// </summary>
    public string? DefaultIntensityCeiling { get; set; }

    /// <summary>
    /// Base stat profile to resolve default session stat seeds for scenario characters.
    /// </summary>
    public string? BaseStatProfileId { get; set; }

    /// <summary>
    /// Resolved stat defaults derived from <see cref="BaseStatProfileId"/>.
    /// Character base stats apply as per-character overrides during session creation.
    /// </summary>
    public Dictionary<string, int> ResolvedBaseStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default role-play perspective mode for the persona when creating sessions from this scenario.
    /// </summary>
    public CharacterPerspectiveMode DefaultPersonaPerspectiveMode { get; set; } = CharacterPerspectiveMode.FirstPersonInternalMonologue;

    /// <summary>
    /// Persisted assistant chat threads for scenario editor assistant usage.
    /// </summary>
    public List<RolePlayAssistantChatThread> AssistantChats { get; set; } = [];

    /// <summary>
    /// Currently selected assistant chat thread ID in scenario editor.
    /// </summary>
    public string? ActiveAssistantChatId { get; set; }
}

public sealed class LegacyScenarioStyle
{
    public string? NarrativeTone { get; set; }
    public string? ProseStyle { get; set; }
    public string? PointOfView { get; set; }
    public List<string> NarrativeGuidelines { get; set; } = [];
    public string? IntensityProfileId { get; set; }
    public string? SteeringProfileId { get; set; }
    public string? IntensityFloor { get; set; }
    public string? IntensityCeiling { get; set; }

    [JsonInclude]
    [JsonPropertyName("StyleGuidelines")]
    public List<string>? LegacyStyleGuidelines
    {
        get => null;
        set
        {
            if (value is not null && NarrativeGuidelines.Count == 0)
            {
                NarrativeGuidelines = value;
            }
        }
    }
}
