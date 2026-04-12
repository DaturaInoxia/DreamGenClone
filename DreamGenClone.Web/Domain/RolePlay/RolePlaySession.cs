using System.Text.Json.Serialization;

namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlaySession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = "Untitled Role-Play";

    public string? ScenarioId { get; set; }

    public RolePlaySessionStatus Status { get; set; } = RolePlaySessionStatus.NotStarted;

    public BehaviorMode BehaviorMode { get; set; } = BehaviorMode.TakeTurns;

    public string? ParentSessionId { get; set; }

    /// <summary>The POV persona name ("You" perspective). Defaults to "You".</summary>
    public string PersonaName { get; set; } = "You";

    /// <summary>Description/content of the POV persona (from template or manual).</summary>
    public string PersonaDescription { get; set; } = string.Empty;

    /// <summary>Active perspective mode for the persona in this session.</summary>
    public CharacterPerspectiveMode PersonaPerspectiveMode { get; set; } = CharacterPerspectiveMode.FirstPersonInternalMonologue;

    /// <summary>Optional link to the Persona template this was sourced from.</summary>
    public string? PersonaTemplateId { get; set; }

    public List<RolePlayInteraction> Interactions { get; set; } = [];

    /// <summary>Active perspective modes for scenario characters in this session.</summary>
    public List<RolePlayCharacterPerspective> CharacterPerspectives { get; set; } = [];

    /// <summary>When true, narrative blocks are auto-generated during overflow continue.</summary>
    public bool AutoNarrative { get; set; } = true;

    /// <summary>Maximum number of scene-character turns to generate per overflow continue click.</summary>
    public int SceneContinueBatchSize { get; set; } = 3;

    /// <summary>Tracks consecutive NPC/bot turns without user input for turn-taking enforcement.</summary>
    public int ConsecutiveNpcTurns { get; set; }

    /// <summary>Whose turn it is in TakeTurns mode. Reset when user submits a message.</summary>
    public TurnState CurrentTurnState { get; set; } = TurnState.Any;

    /// <summary>Number of consecutive NPC turns before signaling it's the user's turn.</summary>
    public int TurnTakingThreshold { get; set; } = 4;

    /// <summary>Number of recent interactions to include in prompt context window.</summary>
    public int ContextWindowSize { get; set; } = 30;

    /// <summary>Persisted session model override ID (null = use function default).</summary>
    public string? SessionModelId { get; set; }

    /// <summary>Persisted temperature override for this session.</summary>
    public double SessionTemperature { get; set; } = 0.7;

    /// <summary>Persisted top-p override for this session.</summary>
    public double SessionTopP { get; set; } = 0.9;

    /// <summary>Persisted max tokens override for this session.</summary>
    public int SessionMaxTokens { get; set; } = 500;

    /// <summary>Persisted assistant model override ID (null = use function default for RolePlayAssistant).</summary>
    public string? AssistantModelId { get; set; }

    /// <summary>Persisted assistant temperature override.</summary>
    public double AssistantTemperature { get; set; } = 0.7;

    /// <summary>Persisted assistant top-p override.</summary>
    public double AssistantTopP { get; set; } = 0.9;

    /// <summary>Persisted assistant max tokens override.</summary>
    public int AssistantMaxTokens { get; set; } = 2000;

    /// <summary>Selected theme profile for this session.</summary>
    [JsonPropertyName("SelectedThemeProfileId")]
    public string? SelectedThemeProfileId { get; set; }

    /// <summary>Legacy alias — maps old SelectedRankingProfileId → SelectedThemeProfileId during deserialization.</summary>
    [JsonInclude]
    [JsonPropertyName("SelectedRankingProfileId")]
    public string? LegacySelectedRankingProfileId
    {
        get => null; // never serialize under old name
        set { if (value is not null && SelectedThemeProfileId is null) SelectedThemeProfileId = value; }
    }

    /// <summary>Selected intensity profile for this session.</summary>
    public string? SelectedIntensityProfileId { get; set; }

    /// <summary>Selected steering profile for this session.</summary>
    public string? SelectedSteeringProfileId { get; set; }

    /// <summary>Session-level intensity floor override.</summary>
    public string? IntensityFloorOverride { get; set; }

    /// <summary>Session-level intensity ceiling override.</summary>
    public string? IntensityCeilingOverride { get; set; }

    /// <summary>When true, the intensity profile is pinned by the user and auto-adaptation is suppressed.</summary>
    public bool IsIntensityManuallyPinned { get; set; }

    /// <summary>Adaptive theme and stat state updated per interaction.</summary>
    public RolePlayAdaptiveState AdaptiveState { get; set; } = new();

    /// <summary>Persisted assistant chat threads for standard chat CRUD behavior.</summary>
    public List<RolePlayAssistantChatThread> AssistantChats { get; set; } = [];

    /// <summary>Currently selected assistant chat thread ID.</summary>
    public string? ActiveAssistantChatId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}

public sealed class RolePlayAssistantChatThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Chat";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<RolePlayAssistantChatMessage> Messages { get; set; } = [];
}

public sealed class RolePlayCharacterPerspective
{
    public string CharacterId { get; set; } = string.Empty;
    public string CharacterName { get; set; } = string.Empty;
    public CharacterPerspectiveMode PerspectiveMode { get; set; } = CharacterPerspectiveMode.ThirdPersonExternalOnly;
}

public sealed class RolePlayAssistantChatMessage
{
    public string Role { get; set; } = "assistant";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
