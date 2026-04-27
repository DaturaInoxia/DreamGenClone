namespace DreamGenClone.Domain.RolePlay;

public enum RPThemeTier
{
    MustHave = 0,
    StronglyPrefer = 1,
    NiceToHave = 2,
    Neutral = 3,
    Discouraged = 4,
    HardDealBreaker = 5
}

public enum RPThemeGuidancePointType
{
    Emphasis = 0,
    Avoidance = 1
}

public enum RPThemeAIGuidanceSection
{
    KeyScenarioElement = 0,
    Avoidance = 1,
    InteractionDynamics = 2,
    ScenarioDistinction = 3,
    Variation = 4,
    FitNote = 5,
    FitFormula = 6,
    FitPattern = 7
}

public sealed class RPThemeProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RPTheme
{
    public string Id { get; set; } = string.Empty;
    public string? ParentThemeId { get; set; }
    public string? NarrativeGateProfileId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Weight { get; set; } = 1;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public List<RPThemeKeyword> Keywords { get; set; } = [];
    public List<RPThemeStatAffinity> StatAffinities { get; set; } = [];
    public List<RPThemePhaseGuidance> PhaseGuidance { get; set; } = [];
    public List<RPThemeGuidancePoint> GuidancePoints { get; set; } = [];
    public List<RPThemeFitRule> FitRules { get; set; } = [];
    public List<RPThemeAIGuidanceNote> AIGenerationNotes { get; set; } = [];
    public List<NarrativeGateRule> NarrativeGateRules { get; set; } = [];
}

public sealed class RPThemeRelationship
{
    public string ParentThemeId { get; set; } = string.Empty;
    public string ChildThemeId { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class RPThemeProfileThemeAssignment
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public RPThemeTier Tier { get; set; } = RPThemeTier.Neutral;
    public decimal Weight { get; set; } = 0m;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class RPThemeKeyword
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class RPThemeStatAffinity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public string StatName { get; set; } = string.Empty;
    public int Value { get; set; }
    public string Rationale { get; set; } = string.Empty;
}

public sealed class RPThemeFitRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public double RoleWeight { get; set; } = 1.0;
    public List<RPThemeFitRuleClause> Clauses { get; set; } = [];
}

public sealed class RPThemeFitRuleClause
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FitRuleId { get; set; } = string.Empty;
    public string StatName { get; set; } = string.Empty;
    public string Comparator { get; set; } = ">=";
    public double Threshold { get; set; }
    public double PenaltyWeight { get; set; } = 1.0;
    public string Description { get; set; } = string.Empty;
}

public sealed class RPThemePhaseGuidance
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public NarrativePhase Phase { get; set; } = NarrativePhase.BuildUp;
    public string GuidanceText { get; set; } = string.Empty;
}

public sealed class RPThemeGuidancePoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public NarrativePhase Phase { get; set; } = NarrativePhase.BuildUp;
    public RPThemeGuidancePointType PointType { get; set; } = RPThemeGuidancePointType.Emphasis;
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class RPThemeAIGuidanceNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ThemeId { get; set; } = string.Empty;
    public RPThemeAIGuidanceSection Section { get; set; } = RPThemeAIGuidanceSection.KeyScenarioElement;
    public string Text { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

public sealed class RPThemeImportRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
    public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;
    public int ImportedCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
}

public sealed class RPThemeImportIssue
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ImportRunId { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string Severity { get; set; } = "Warning";
    public string Message { get; set; } = string.Empty;
}

public sealed class RPFinishingMoveMatrixRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; set; } = string.Empty;
    public string DesireBand { get; set; } = "50-74";
    public string SelfRespectBand { get; set; } = "30-59";
    public string DominanceBand { get; set; } = "Medium";
    public List<string> PrimaryLocations { get; set; } = [];
    public List<string> SecondaryLocations { get; set; } = [];
    public List<string> ExcludedLocations { get; set; } = [];
    public string WifeBehaviorModifier { get; set; } = string.Empty;
    public string OtherManBehaviorModifier { get; set; } = string.Empty;
    public string TransitionInstruction { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RPSteerPositionMatrixRow
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProfileId { get; set; } = string.Empty;
    public string DesireBand { get; set; } = "30-59";
    public string SelfRespectBand { get; set; } = "30-59";
    public string WifeDominanceBand { get; set; } = "Medium";
    public string OtherManDominanceBand { get; set; } = "Medium";
    public List<string> PrimaryPositions { get; set; } = [];
    public List<string> SecondaryPositions { get; set; } = [];
    public List<string> ExcludedPositions { get; set; } = [];
    public string WifeBehaviorModifier { get; set; } = string.Empty;
    public string OtherManBehaviorModifier { get; set; } = string.Empty;
    public string TransitionInstruction { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
