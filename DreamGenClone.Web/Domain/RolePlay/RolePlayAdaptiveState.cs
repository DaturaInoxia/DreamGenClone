using DreamGenClone.Domain.StoryAnalysis;

namespace DreamGenClone.Web.Domain.RolePlay;

public sealed class RolePlayAdaptiveState
{
    public Dictionary<string, CharacterStatBlock> CharacterStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, PairwiseStatBlock> PairwiseStats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ThemeTrackerState ThemeTracker { get; set; } = new();

    public string? ActiveScenarioId { get; set; }

    public string? ActiveVariantId { get; set; }

    public string? SelectedWillingnessProfileId { get; set; }

    public string? HusbandAwarenessProfileId { get; set; }

    public DateTime? ScenarioCommitmentTimeUtc { get; set; }

    public NarrativePhase CurrentNarrativePhase { get; set; } = NarrativePhase.BuildUp;

    public int CompletedScenarios { get; set; }

    public int InteractionsSinceCommitment { get; set; }

    public int InteractionsInApproaching { get; set; }

    public List<ScenarioMetadata> ScenarioHistory { get; set; } = [];
}

public sealed class CharacterStatBlock
{
    public string CharacterId { get; set; } = string.Empty;

    public Dictionary<string, int> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PairwiseStatBlock
{
    public string SourceCharacterId { get; set; } = string.Empty;

    public string TargetCharacterId { get; set; } = string.Empty;

    public Dictionary<string, int> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeTrackerState
{
    public Dictionary<string, ThemeTrackerItem> Themes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ThemeEvidenceEvent> RecentEvidence { get; set; } = [];

    public string? PrimaryThemeId { get; set; }

    public string? SecondaryThemeId { get; set; }

    public string ThemeSelectionRule { get; set; } = "Top1";

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ThemeTrackerItem
{
    public string ThemeId { get; set; } = string.Empty;

    public string ThemeName { get; set; } = string.Empty;

    public string Intensity { get; set; } = "None";

    public double Score { get; set; }

    public ThemeScoreBreakdown Breakdown { get; set; } = new();

    public bool Blocked { get; set; }

    public int SuppressedHitCount { get; set; }

    public bool IsScenarioCandidate { get; set; }

    public double NarrativeFitScore { get; set; }

    public DateTime? LastCandidateEvaluationTimeUtc { get; set; }
}

public sealed class ThemeScoreBreakdown
{
    public double ChoiceSignal { get; set; }

    public double CharacterStateSignal { get; set; }

    public double InteractionEvidenceSignal { get; set; }

    public double ScenarioPhaseSignal { get; set; }
}

public sealed class ThemeEvidenceEvent
{
    public string InteractionId { get; set; } = string.Empty;

    public string ThemeId { get; set; } = string.Empty;

    public string SignalType { get; set; } = string.Empty;

    public double Delta { get; set; }

    public double Confidence { get; set; }

    public string Rationale { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}