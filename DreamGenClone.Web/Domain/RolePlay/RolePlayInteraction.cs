namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlayInteraction
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public InteractionType InteractionType { get; set; }

    public string ActorName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsExcluded { get; set; }

    public bool IsHidden { get; set; }

    public bool IsPinned { get; set; }

    public string? ParentInteractionId { get; set; }

    public int AlternativeIndex { get; set; }

    public int ActiveAlternativeIndex { get; set; }

    /// <summary>Model identifier used to generate this interaction (null for user-authored).</summary>
    public string? GeneratedByModelId { get; set; }

    /// <summary>Display name of the model used for generation.</summary>
    public string? GeneratedByModelName { get; set; }

    /// <summary>The command that created this interaction (e.g. Retry, MakeLonger, AskToRewrite).</summary>
    public string? GeneratedByCommand { get; set; }

    /// <summary>Provider name of the model used for generation.</summary>
    public string? GeneratedByProvider { get; set; }

    /// <summary>Temperature used during generation.</summary>
    public double? GeneratedTemperature { get; set; }

    /// <summary>Top-P used during generation.</summary>
    public double? GeneratedTopP { get; set; }

    /// <summary>Max tokens setting used during generation.</summary>
    public int? GeneratedMaxTokens { get; set; }
}
