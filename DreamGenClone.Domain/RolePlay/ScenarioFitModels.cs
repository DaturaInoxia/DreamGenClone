namespace DreamGenClone.Domain.RolePlay;

public sealed class ScenarioFitRules
{
    public Dictionary<string, string> RoleCharacterBindings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<CharacterRoleRule> CharacterRoleRules { get; set; } = [];

    public Dictionary<string, double> CharacterRoleWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ScenarioModifierRule> ScenarioModifiers { get; set; } = [];
}

public sealed class CharacterRoleRule
{
    public string RoleName { get; set; } = string.Empty;

    public Dictionary<string, StatThresholdSpecification> StatThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> StatWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class StatThresholdSpecification
{
    public double? MinimumValue { get; set; }

    public double? MaximumValue { get; set; }

    public double? OptimalMin { get; set; }

    public double? OptimalMax { get; set; }

    public double PenaltyWeight { get; set; } = 1.0;
}

public sealed class ScenarioModifierRule
{
    public string Type { get; set; } = string.Empty;

    public double Value { get; set; }

    public string? RoleName { get; set; }
}

public sealed class ScenarioFitResult
{
    public string ScenarioId { get; set; } = string.Empty;

    public double FitScore { get; set; }

    public Dictionary<string, double> CharacterRoleScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string Rationale { get; set; } = string.Empty;

    public List<string> Failures { get; set; } = [];
}