using System.Text.RegularExpressions;
using System.Text.Json;
using System.Globalization;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed partial class RPThemeService : IRPThemeService
{
    private const string AutoBackfillRationale = "auto-backfilled for canonical stat parity";
    private static readonly HashSet<string> SupportedSemanticStatKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Desire",
        "Restraint",
        "Tension",
        "Connection",
        "Dominance",
        "Loyalty",
        "SelfRespect"
    };
    private static readonly (string From, string To)[] RequiredNarrativeTransitions =
    [
        ("BuildUp", "Committed"),
        ("Committed", "Approaching"),
        ("Approaching", "Climax"),
        ("Climax", "Reset"),
        ("Reset", "BuildUp")
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly string _connectionString;
    private readonly ILogger<RPThemeService> _logger;
    private readonly IThemeMachineAuthorizationService? _themeMachineAuthorizationService;
    private bool? _rpThemesHasProfileIdColumn;
    private bool? _rpThemesHasNarrativeGateProfileIdColumn;
    private bool _supplementalTablesEnsured;

    public RPThemeService(
        IOptions<PersistenceOptions> options,
        ILogger<RPThemeService> logger,
        IThemeMachineAuthorizationService? themeMachineAuthorizationService = null)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
        _themeMachineAuthorizationService = themeMachineAuthorizationService;
    }

    public async Task<RPThemeProfile> SaveProfileAsync(RPThemeProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim();
        profile.Name = (profile.Name ?? string.Empty).Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.UpdatedUtc = DateTime.UtcNow;
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = profile.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("RP theme profile name is required.", nameof(profile));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfiles (Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc, ThemeSelectionTurnsPerTheme)
            VALUES ($id, $name, $description, $isDefault, $createdUtc, $updatedUtc, $selectionMultiplier)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                IsDefault = excluded.IsDefault,
                UpdatedUtc = excluded.UpdatedUtc,
                ThemeSelectionTurnsPerTheme = excluded.ThemeSelectionTurnsPerTheme;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", profile.UpdatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$selectionMultiplier", profile.ThemeSelectionTurnsPerTheme);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return profile;
    }

    public async Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<RPThemeProfile>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc, ThemeSelectionTurnsPerTheme FROM RPThemeProfiles ORDER BY Name";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            profiles.Add(new RPThemeProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Description = reader.GetString(2),
                IsDefault = reader.GetInt32(3) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow,
                ThemeSelectionTurnsPerTheme = reader.GetInt32(6)
            });
        }

        return profiles;
    }

    public async Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc, ThemeSelectionTurnsPerTheme FROM RPThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RPThemeProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            IsDefault = reader.GetInt32(3) == 1,
            CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow,
            ThemeSelectionTurnsPerTheme = reader.GetInt32(6)
        };
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<RPTheme> SaveThemeAsync(RPTheme theme, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(theme);

        theme.Id = string.IsNullOrWhiteSpace(theme.Id) ? Guid.NewGuid().ToString("N") : theme.Id.Trim();
        theme.Label = (theme.Label ?? string.Empty).Trim();
        theme.Description = (theme.Description ?? string.Empty).Trim();
        theme.Category = (theme.Category ?? string.Empty).Trim();
        theme.Weight = Math.Clamp(theme.Weight, 1, 10);
        theme.UpdatedUtc = DateTime.UtcNow;
        if (theme.CreatedUtc == default)
        {
            theme.CreatedUtc = theme.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(theme.Label))
        {
            throw new ArgumentException("Theme label is required.", nameof(theme));
        }

        theme.SuccessorThemeLinks = NormalizeSuccessorThemeLinks(theme.Id, theme.SuccessorThemeLinks);
        theme.NarrativeGateRules = NormalizeNarrativeGateRules(theme.NarrativeGateRules);

        EnsureCanonicalStatAffinities(theme);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        ValidateRequiredNarrativeTransitions(theme.NarrativeGateRules);
        await ValidateSuccessorThemeLinksAsync(connection, theme, cancellationToken);
        theme.NarrativeGateProfileId = null;

        await EnsureGlobalThemeLibraryProfileAsync(connection, cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            var hasProfileId = await RPThemesTableHasProfileIdAsync(connection, cancellationToken);
            var hasNarrativeGateProfileId = await RPThemesTableHasNarrativeGateProfileIdAsync(connection, cancellationToken);

            if (hasProfileId && hasNarrativeGateProfileId)
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, ProfileId, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $profileId, $narrativeGateProfileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        ProfileId = excluded.ProfileId,
                        NarrativeGateProfileId = excluded.NarrativeGateProfileId,
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$profileId", IRPThemeService.GlobalThemeLibraryProfileId);
            }
            else if (hasNarrativeGateProfileId)
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $narrativeGateProfileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        NarrativeGateProfileId = excluded.NarrativeGateProfileId,
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
            }
            else if (hasProfileId)
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, ProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $profileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        ProfileId = excluded.ProfileId,
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
                command.Parameters.AddWithValue("$profileId", IRPThemeService.GlobalThemeLibraryProfileId);
            }
            else
            {
                command.CommandText = """
                    INSERT INTO RPThemes (Id, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                    VALUES ($id, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                    ON CONFLICT(Id) DO UPDATE SET
                        Label = excluded.Label,
                        Description = excluded.Description,
                        Category = excluded.Category,
                        Weight = excluded.Weight,
                        IsEnabled = excluded.IsEnabled,
                        UpdatedUtc = excluded.UpdatedUtc;
                    """;
            }

            if (hasNarrativeGateProfileId)
            {
                command.Parameters.AddWithValue("$narrativeGateProfileId", (object?)theme.NarrativeGateProfileId ?? DBNull.Value);
            }
            command.Parameters.AddWithValue("$id", theme.Id);
            command.Parameters.AddWithValue("$label", theme.Label);
            command.Parameters.AddWithValue("$description", theme.Description);
            command.Parameters.AddWithValue("$category", theme.Category);
            command.Parameters.AddWithValue("$weight", theme.Weight);
            command.Parameters.AddWithValue("$isEnabled", theme.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", theme.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", theme.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);

        await using (var deleteHierarchy = connection.CreateCommand())
        {
            deleteHierarchy.Transaction = tx;
            deleteHierarchy.CommandText = "DELETE FROM RPThemeRelationships WHERE ChildThemeId = $themeId";
            deleteHierarchy.Parameters.AddWithValue("$themeId", theme.Id);
            await deleteHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(theme.ParentThemeId))
        {
            await using var insertHierarchy = connection.CreateCommand();
            insertHierarchy.Transaction = tx;
            insertHierarchy.CommandText = """
                INSERT INTO RPThemeRelationships (ParentThemeId, ChildThemeId, SortOrder)
                VALUES ($parentThemeId, $childThemeId, $sortOrder)
                ON CONFLICT(ParentThemeId, ChildThemeId) DO UPDATE SET SortOrder = excluded.SortOrder;
                """;
            insertHierarchy.Parameters.AddWithValue("$parentThemeId", theme.ParentThemeId);
            insertHierarchy.Parameters.AddWithValue("$childThemeId", theme.Id);
            insertHierarchy.Parameters.AddWithValue("$sortOrder", 0);
            await insertHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        return theme;
    }

    public async Task<RPTheme> CloneThemeAsync(string sourceThemeId, string newThemeId, string newThemeLabel, CancellationToken cancellationToken = default)
    {
        sourceThemeId = (sourceThemeId ?? string.Empty).Trim();
        newThemeId = (newThemeId ?? string.Empty).Trim();
        newThemeLabel = (newThemeLabel ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(sourceThemeId))
        {
            throw new ArgumentException("Source theme Id is required.", nameof(sourceThemeId));
        }

        if (string.IsNullOrWhiteSpace(newThemeId))
        {
            throw new ArgumentException("New theme Id is required.", nameof(newThemeId));
        }

        if (string.IsNullOrWhiteSpace(newThemeLabel))
        {
            throw new ArgumentException("New theme label is required.", nameof(newThemeLabel));
        }

        if (string.Equals(sourceThemeId, newThemeId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Clone Id must be different from source theme Id.");
        }

        var sourceTheme = await GetThemeAsync(sourceThemeId, cancellationToken);
        if (sourceTheme is null)
        {
            throw new InvalidOperationException($"Theme '{sourceThemeId}' was not found.");
        }

        var existingTarget = await GetThemeAsync(newThemeId, cancellationToken);
        if (existingTarget is not null)
        {
            throw new InvalidOperationException($"Theme Id '{newThemeId}' already exists.");
        }

        var clonedTheme = new RPTheme
        {
            Id = newThemeId,
            ParentThemeId = sourceTheme.ParentThemeId,
            NarrativeGateProfileId = sourceTheme.NarrativeGateProfileId,
            Label = newThemeLabel,
            Description = sourceTheme.Description,
            Category = sourceTheme.Category,
            Weight = sourceTheme.Weight,
            IsEnabled = sourceTheme.IsEnabled,
            Keywords = sourceTheme.Keywords
                .Select(keyword => new RPThemeKeyword
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    GroupName = keyword.GroupName,
                    Keyword = keyword.Keyword,
                    SortOrder = keyword.SortOrder
                })
                .ToList(),
            StatAffinities = sourceTheme.StatAffinities
                .Select(affinity => new RPThemeStatAffinity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    StatName = affinity.StatName,
                    Value = affinity.Value,
                    Rationale = affinity.Rationale
                })
                .ToList(),
            PhaseGuidance = sourceTheme.PhaseGuidance
                .Select(guidance => new RPThemePhaseGuidance
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    Phase = guidance.Phase,
                    GuidanceText = guidance.GuidanceText,
                    DirectiveText = guidance.DirectiveText
                })
                .ToList(),
            GuidancePoints = sourceTheme.GuidancePoints
                .Select(point => new RPThemeGuidancePoint
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    Phase = point.Phase,
                    PointType = point.PointType,
                    Text = point.Text,
                    SortOrder = point.SortOrder
                })
                .ToList(),
            FitRules = sourceTheme.FitRules
                .Select(rule =>
                {
                    var clonedRuleId = Guid.NewGuid().ToString("N");
                    return new RPThemeFitRule
                    {
                        Id = clonedRuleId,
                        ThemeId = newThemeId,
                        RoleName = rule.RoleName,
                        RoleWeight = rule.RoleWeight,
                        Clauses = rule.Clauses
                            .Select(clause => new RPThemeFitRuleClause
                            {
                                Id = Guid.NewGuid().ToString("N"),
                                FitRuleId = clonedRuleId,
                                StatName = clause.StatName,
                                Comparator = clause.Comparator,
                                Threshold = clause.Threshold,
                                PenaltyWeight = clause.PenaltyWeight,
                                Description = clause.Description
                            })
                            .ToList()
                    };
                })
                .ToList(),
            AIGenerationNotes = sourceTheme.AIGenerationNotes
                .Select(note => new RPThemeAIGuidanceNote
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    Section = note.Section,
                    Text = note.Text,
                    SortOrder = note.SortOrder
                })
                .ToList(),
            SemanticEventMappings = sourceTheme.SemanticEventMappings
                .Select(mapping => new RPSemanticEventMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    EventId = mapping.EventId,
                    Direction = mapping.Direction,
                    Delta = mapping.Delta,
                    ConfidenceMin = mapping.ConfidenceMin,
                    ConfidenceMax = mapping.ConfidenceMax,
                    ReasonCode = mapping.ReasonCode,
                    AttributionKey = mapping.AttributionKey,
                    SortOrder = mapping.SortOrder
                })
                .ToList(),
            SemanticStatMappings = sourceTheme.SemanticStatMappings
                .Select(mapping => new RPSemanticStatMapping
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    EventId = mapping.EventId,
                    TargetStat = mapping.TargetStat,
                    Direction = mapping.Direction,
                    Delta = mapping.Delta,
                    ConfidenceMin = mapping.ConfidenceMin,
                    ConfidenceMax = mapping.ConfidenceMax,
                    ReasonCode = mapping.ReasonCode,
                    AttributionKey = mapping.AttributionKey,
                    SortOrder = mapping.SortOrder
                })
                .ToList(),
            NarrativeGateRules = sourceTheme.NarrativeGateRules
                .Select(rule => new NarrativeGateRule
                {
                    SortOrder = rule.SortOrder,
                    FromPhase = rule.FromPhase,
                    ToPhase = rule.ToPhase,
                    MetricKey = rule.MetricKey,
                    Comparator = rule.Comparator,
                    Threshold = rule.Threshold
                })
                .ToList(),
            SuccessorThemeLinks = sourceTheme.SuccessorThemeLinks
                .Select((link, index) => new RPThemeSuccessorLink
                {
                    SourceThemeId = newThemeId,
                    SuccessorThemeId = link.SuccessorThemeId,
                    ScoreBoost = link.ScoreBoost,
                    SortOrder = index + 1
                })
                .ToList(),
            StatDecayOverrides = sourceTheme.StatDecayOverrides
                .Select(o => new RPThemeStatDecayOverride
                {
                    Id = Guid.NewGuid().ToString("N"),
                    ThemeId = newThemeId,
                    StatName = o.StatName,
                    DecayScale = o.DecayScale,
                    Description = o.Description
                })
                .ToList()
        };

        return await SaveThemeAsync(clonedTheme, cancellationToken);
    }

    public async Task<IReadOnlyList<RPTheme>> ListThemesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var themes = new List<RPTheme>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes ORDER BY Label"
            : "SELECT Id, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes WHERE IsEnabled = 1 ORDER BY Label";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            themes.Add(new RPTheme
            {
                Id = reader.GetString(0),
                NarrativeGateProfileId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Label = reader.GetString(2),
                Description = reader.GetString(3),
                Category = reader.GetString(4),
                Weight = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var updated) ? updated : DateTime.UtcNow
            });
        }

        foreach (var theme in themes)
        {
            theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
            theme.SuccessorThemeLinks = await LoadThemeSuccessorLinksAsync(connection, theme.Id, cancellationToken);
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.StatDecayOverrides = await LoadThemeStatDecayOverridesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
            theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
            theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
            theme.SemanticEventMappings = await LoadThemeSemanticEventMappingsAsync(connection, theme.Id, cancellationToken);
            theme.SemanticStatMappings = await LoadThemeSemanticStatMappingsAsync(connection, theme.Id, cancellationToken);
            theme.NarrativeGateRules = await LoadThemeNarrativeGateRulesAsync(connection, theme.Id, cancellationToken);
            await EnsureCanonicalStatAffinitiesPersistedAsync(connection, theme, cancellationToken);
            await EnsureThemeNarrativeGateRulesPersistedAsync(connection, theme, cancellationToken);
        }

        return themes;
    }

    public async Task<IReadOnlyList<RPTheme>> ListThemesByProfileAsync(string profileId, bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var themes = new List<RPTheme>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? """
                                                        SELECT DISTINCT t.Id, t.NarrativeGateProfileId, t.Label, t.Description, t.Category, t.Weight, t.IsEnabled, t.CreatedUtc, t.UpdatedUtc
              FROM RPThemes t
              INNER JOIN RPThemeProfileThemeAssignments a ON a.ThemeId = t.Id
              WHERE a.ProfileId = $profileId AND a.IsEnabled = 1
              ORDER BY t.Label
              """
            : """
                                                        SELECT DISTINCT t.Id, t.NarrativeGateProfileId, t.Label, t.Description, t.Category, t.Weight, t.IsEnabled, t.CreatedUtc, t.UpdatedUtc
              FROM RPThemes t
              INNER JOIN RPThemeProfileThemeAssignments a ON a.ThemeId = t.Id
              WHERE a.ProfileId = $profileId AND a.IsEnabled = 1 AND t.IsEnabled = 1
              ORDER BY t.Label
              """;
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            themes.Add(new RPTheme
            {
                Id = reader.GetString(0),
                NarrativeGateProfileId = reader.IsDBNull(1) ? null : reader.GetString(1),
                Label = reader.GetString(2),
                Description = reader.GetString(3),
                Category = reader.GetString(4),
                Weight = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var updated) ? updated : DateTime.UtcNow
            });
        }

        foreach (var theme in themes)
        {
            theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
            theme.SuccessorThemeLinks = await LoadThemeSuccessorLinksAsync(connection, theme.Id, cancellationToken);
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.StatDecayOverrides = await LoadThemeStatDecayOverridesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
            theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
            theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
            theme.SemanticEventMappings = await LoadThemeSemanticEventMappingsAsync(connection, theme.Id, cancellationToken);
            theme.SemanticStatMappings = await LoadThemeSemanticStatMappingsAsync(connection, theme.Id, cancellationToken);
            theme.NarrativeGateRules = await LoadThemeNarrativeGateRulesAsync(connection, theme.Id, cancellationToken);
            await EnsureCanonicalStatAffinitiesPersistedAsync(connection, theme, cancellationToken);
            await EnsureThemeNarrativeGateRulesPersistedAsync(connection, theme, cancellationToken);
        }

        return themes;
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>> ResolveSemanticEventMappingsByThemeIdsAsync(
        IEnumerable<string> themeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(themeIds);
        var ids = themeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Semantic mapping resolution requires at least one theme id.");
        }

        var themes = new List<RPTheme>(ids.Count);
        foreach (var id in ids)
        {
            var theme = await GetThemeAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"RP theme '{id}' referenced by session theme selection was not found.");
            if (!theme.IsEnabled)
            {
                throw new InvalidOperationException($"RP theme '{id}' referenced by session theme selection is disabled.");
            }
            themes.Add(theme);
        }

        var mappingsByEvent = new Dictionary<string, List<RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in themes)
        {
            foreach (var mapping in theme.SemanticEventMappings)
            {
                if (!mappingsByEvent.TryGetValue(mapping.EventId, out var bucket))
                {
                    bucket = [];
                    mappingsByEvent[mapping.EventId] = bucket;
                }
                bucket.Add(mapping);
            }
        }

        if (mappingsByEvent.Count == 0)
        {
            throw new InvalidOperationException(
                $"No semantic event mapping configuration found for session theme ids [{string.Join(", ", ids)}]. Add semantic mappings in the theme Semantic Event Mappings section.");
        }

        return mappingsByEvent.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RPSemanticEventMapping>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticStatMapping>>> ResolveSemanticStatMappingsByThemeIdsAsync(
        IEnumerable<string> themeIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(themeIds);
        var ids = themeIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException("Semantic stat mapping resolution requires at least one theme id.");
        }

        var themes = new List<RPTheme>(ids.Count);
        foreach (var id in ids)
        {
            var theme = await GetThemeAsync(id, cancellationToken)
                ?? throw new InvalidOperationException($"RP theme '{id}' referenced by session theme selection was not found.");
            if (!theme.IsEnabled)
            {
                throw new InvalidOperationException($"RP theme '{id}' referenced by session theme selection is disabled.");
            }
            themes.Add(theme);
        }

        var mappingsByEvent = new Dictionary<string, List<RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase);
        foreach (var theme in themes)
        {
            foreach (var mapping in theme.SemanticStatMappings)
            {
                if (!mappingsByEvent.TryGetValue(mapping.EventId, out var bucket))
                {
                    bucket = [];
                    mappingsByEvent[mapping.EventId] = bucket;
                }
                bucket.Add(mapping);
            }
        }

        if (mappingsByEvent.Count == 0)
        {
            throw new InvalidOperationException(
                $"No semantic stat mapping configuration found for session theme ids [{string.Join(", ", ids)}]. Add semantic stat mappings in the theme Semantic Stat Mappings section.");
        }

        return mappingsByEvent.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RPSemanticStatMapping>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticEventMapping>>> ResolveSemanticEventMappingsByProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException("Semantic mapping resolution requires a profile id.");
        }

        var themes = await ListThemesByProfileAsync(profileId.Trim(), includeDisabled: false, cancellationToken);
        if (themes.Count == 0)
        {
            throw new InvalidOperationException($"No enabled RP themes are assigned to profile '{profileId}'.");
        }

        var mappingsByEvent = new Dictionary<string, List<RPSemanticEventMapping>>(StringComparer.OrdinalIgnoreCase);

        foreach (var theme in themes)
        {
            foreach (var mapping in theme.SemanticEventMappings)
            {
                if (!mappingsByEvent.TryGetValue(mapping.EventId, out var bucket))
                {
                    bucket = [];
                    mappingsByEvent[mapping.EventId] = bucket;
                }

                bucket.Add(mapping);
            }
        }

        if (mappingsByEvent.Count == 0)
        {
            throw new InvalidOperationException(
                $"No semantic mapping configuration found for profile '{profileId}'. Add semantic mappings in the theme Semantic Event Mappings section.");
        }

        return mappingsByEvent.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RPSemanticEventMapping>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<RPSemanticStatMapping>>> ResolveSemanticStatMappingsByProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new InvalidOperationException("Semantic stat mapping resolution requires a profile id.");
        }

        var themes = await ListThemesByProfileAsync(profileId.Trim(), includeDisabled: false, cancellationToken);
        if (themes.Count == 0)
        {
            throw new InvalidOperationException($"No enabled RP themes are assigned to profile '{profileId}'.");
        }

        var mappingsByEvent = new Dictionary<string, List<RPSemanticStatMapping>>(StringComparer.OrdinalIgnoreCase);

        foreach (var theme in themes)
        {
            foreach (var mapping in theme.SemanticStatMappings)
            {
                if (!mappingsByEvent.TryGetValue(mapping.EventId, out var bucket))
                {
                    bucket = [];
                    mappingsByEvent[mapping.EventId] = bucket;
                }

                bucket.Add(mapping);
            }
        }

        if (mappingsByEvent.Count == 0)
        {
            throw new InvalidOperationException(
                $"No semantic stat mapping configuration found for profile '{profileId}'. Add semantic stat mappings in the theme Semantic Stat Mappings section.");
        }

        return mappingsByEvent.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<RPSemanticStatMapping>)x.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    public async Task<RPTheme?> GetThemeAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc FROM RPThemes WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var theme = new RPTheme
        {
            Id = reader.GetString(0),
            NarrativeGateProfileId = reader.IsDBNull(1) ? null : reader.GetString(1),
            Label = reader.GetString(2),
            Description = reader.GetString(3),
            Category = reader.GetString(4),
            Weight = reader.GetInt32(5),
            IsEnabled = reader.GetInt32(6) == 1,
            CreatedUtc = DateTime.TryParse(reader.GetString(7), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var updated) ? updated : DateTime.UtcNow
        };

        theme.ParentThemeId = await LoadParentThemeIdAsync(connection, theme.Id, cancellationToken);
        theme.SuccessorThemeLinks = await LoadThemeSuccessorLinksAsync(connection, theme.Id, cancellationToken);
        theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
        theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
        theme.StatDecayOverrides = await LoadThemeStatDecayOverridesAsync(connection, theme.Id, cancellationToken);
        theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
        theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
        theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
        theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
        theme.SemanticEventMappings = await LoadThemeSemanticEventMappingsAsync(connection, theme.Id, cancellationToken);
        theme.SemanticStatMappings = await LoadThemeSemanticStatMappingsAsync(connection, theme.Id, cancellationToken);
        theme.NarrativeGateRules = await LoadThemeNarrativeGateRulesAsync(connection, theme.Id, cancellationToken);
        await EnsureCanonicalStatAffinitiesPersistedAsync(connection, theme, cancellationToken);
        await EnsureThemeNarrativeGateRulesPersistedAsync(connection, theme, cancellationToken);

        return theme;
    }

    public async Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);

        await using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.CommandText = "SELECT COUNT(*) FROM RPThemeProfileThemeAssignments WHERE ThemeId = $id";
            checkCommand.Parameters.AddWithValue("$id", id);
            var assignmentCount = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken));
            if (assignmentCount > 0)
            {
                _logger.LogInformation("Skipped deleting RP theme {ThemeId} because it is referenced by {AssignmentCount} profile assignments.", id, assignmentCount);
                return false;
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemes WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<RPThemeMachineDefinition> SaveMachineDefinitionAsync(RPThemeMachineDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var now = DateTime.UtcNow;
        definition.DefinitionId = string.IsNullOrWhiteSpace(definition.DefinitionId) ? Guid.NewGuid().ToString("N") : definition.DefinitionId.Trim();
        definition.ThemeId = (definition.ThemeId ?? string.Empty).Trim();
        definition.MachineKey = (definition.MachineKey ?? string.Empty).Trim();
        definition.Name = (definition.Name ?? string.Empty).Trim();
        definition.Version = definition.Version <= 0
            ? throw new ArgumentException("Machine definition version must be greater than zero.", nameof(definition))
            : definition.Version;
        definition.CreatedUtc = definition.CreatedUtc == default ? now : definition.CreatedUtc;
        definition.UpdatedUtc = now;

        if (string.IsNullOrWhiteSpace(definition.ThemeId))
        {
            throw new ArgumentException("ThemeId is required for machine definition persistence.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.MachineKey))
        {
            throw new ArgumentException("MachineKey is required for machine definition persistence.", nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException("Machine definition name is required.", nameof(definition));
        }

        definition.States = NormalizeMachineStates(definition.DefinitionId, definition.States);
        definition.Transitions = NormalizeMachineTransitions(definition.DefinitionId, definition.Transitions, now);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);

        var definitionAlreadyExists = await MachineDefinitionExistsAsync(connection, definition.DefinitionId, cancellationToken);

        if (!await ThemeExistsAsync(connection, definition.ThemeId, cancellationToken))
        {
            throw new InvalidOperationException($"Cannot save machine definition '{definition.DefinitionId}': theme '{definition.ThemeId}' does not exist.");
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO RPThemeMachineDefinitions (
                    DefinitionId, ThemeId, MachineKey, Version, Name, IsActive, IsSeeded, CreatedUtc, UpdatedUtc)
                VALUES (
                    $definitionId, $themeId, $machineKey, $version, $name, $isActive, $isSeeded, $createdUtc, $updatedUtc)
                ON CONFLICT(DefinitionId) DO UPDATE SET
                    ThemeId = excluded.ThemeId,
                    MachineKey = excluded.MachineKey,
                    Version = excluded.Version,
                    Name = excluded.Name,
                    IsActive = excluded.IsActive,
                    IsSeeded = excluded.IsSeeded,
                    UpdatedUtc = excluded.UpdatedUtc;
                """;
            command.Parameters.AddWithValue("$definitionId", definition.DefinitionId);
            command.Parameters.AddWithValue("$themeId", definition.ThemeId);
            command.Parameters.AddWithValue("$machineKey", definition.MachineKey);
            command.Parameters.AddWithValue("$version", definition.Version);
            command.Parameters.AddWithValue("$name", definition.Name);
            command.Parameters.AddWithValue("$isActive", definition.IsActive ? 1 : 0);
            command.Parameters.AddWithValue("$isSeeded", definition.IsSeeded ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", definition.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", definition.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteTransitions = connection.CreateCommand())
        {
            deleteTransitions.Transaction = tx;
            deleteTransitions.CommandText = "DELETE FROM RPThemeMachineTransitions WHERE DefinitionId = $definitionId";
            deleteTransitions.Parameters.AddWithValue("$definitionId", definition.DefinitionId);
            await deleteTransitions.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var deleteStates = connection.CreateCommand())
        {
            deleteStates.Transaction = tx;
            deleteStates.CommandText = "DELETE FROM RPThemeMachineStates WHERE DefinitionId = $definitionId";
            deleteStates.Parameters.AddWithValue("$definitionId", definition.DefinitionId);
            await deleteStates.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var state in definition.States)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO RPThemeMachineStates (
                    StateId, DefinitionId, StateCode, Label, IsInitial, IsTerminal, SortOrder)
                VALUES (
                    $stateId, $definitionId, $stateCode, $label, $isInitial, $isTerminal, $sortOrder);
                """;
            command.Parameters.AddWithValue("$stateId", state.StateId);
            command.Parameters.AddWithValue("$definitionId", state.DefinitionId);
            command.Parameters.AddWithValue("$stateCode", state.StateCode);
            command.Parameters.AddWithValue("$label", state.Label);
            command.Parameters.AddWithValue("$isInitial", state.IsInitial ? 1 : 0);
            command.Parameters.AddWithValue("$isTerminal", state.IsTerminal ? 1 : 0);
            command.Parameters.AddWithValue("$sortOrder", state.SortOrder);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var transition in definition.Transitions)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = """
                INSERT INTO RPThemeMachineTransitions (
                    TransitionId, DefinitionId, FromStateCode, ToStateCode, Priority, TriggerType,
                    GateConfigJson, BlockReasonCode, IsEnabled, CreatedUtc, UpdatedUtc)
                VALUES (
                    $transitionId, $definitionId, $fromStateCode, $toStateCode, $priority, $triggerType,
                    $gateConfigJson, $blockReasonCode, $isEnabled, $createdUtc, $updatedUtc);
                """;
            command.Parameters.AddWithValue("$transitionId", transition.TransitionId);
            command.Parameters.AddWithValue("$definitionId", transition.DefinitionId);
            command.Parameters.AddWithValue("$fromStateCode", transition.FromStateCode);
            command.Parameters.AddWithValue("$toStateCode", transition.ToStateCode);
            command.Parameters.AddWithValue("$priority", transition.Priority);
            command.Parameters.AddWithValue("$triggerType", transition.TriggerType);
            command.Parameters.AddWithValue("$gateConfigJson", transition.GateConfigJson);
            command.Parameters.AddWithValue("$blockReasonCode", transition.BlockReasonCode);
            command.Parameters.AddWithValue("$isEnabled", transition.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", transition.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", transition.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Theme machine definition {Operation}: DefinitionId={DefinitionId} ThemeId={ThemeId} MachineKey={MachineKey} Version={Version} StateCount={StateCount} TransitionCount={TransitionCount}",
            definitionAlreadyExists ? "updated" : "created",
            definition.DefinitionId,
            definition.ThemeId,
            definition.MachineKey,
            definition.Version,
            definition.States.Count,
            definition.Transitions.Count);

        return await GetMachineDefinitionCoreAsync(connection, definition.DefinitionId, cancellationToken)
            ?? throw new InvalidOperationException($"Saved machine definition '{definition.DefinitionId}' could not be reloaded.");
    }

    public async Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsAsync(string themeId, CancellationToken cancellationToken = default)
    {
        themeId = (themeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(themeId))
        {
            throw new ArgumentException("ThemeId is required to list machine definitions.", nameof(themeId));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);
        return await ListMachineDefinitionsCoreAsync(connection, themeId, cancellationToken);
    }

    public async Task<RPThemeMachineDefinition?> GetMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        definitionId = (definitionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            throw new ArgumentException("DefinitionId is required to get machine definition.", nameof(definitionId));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);
        return await GetMachineDefinitionCoreAsync(connection, definitionId, cancellationToken);
    }

    public async Task ActivateMachineDefinitionAsync(string themeId, string machineKey, int version, string actorId, CancellationToken cancellationToken = default)
    {
        themeId = (themeId ?? string.Empty).Trim();
        machineKey = (machineKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(themeId) || string.IsNullOrWhiteSpace(machineKey) || version <= 0)
        {
            throw new ArgumentException("ThemeId, machineKey, and version are required to activate a machine definition.");
        }

        await EnsureMachineMutationAuthorizedAsync($"theme:{themeId}", actorId, "activate", cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);

        var definition = await GetMachineDefinitionByThemeKeyVersionAsync(connection, themeId, machineKey, version, cancellationToken)
            ?? throw new InvalidOperationException($"Cannot activate machine definition: '{themeId}/{machineKey}/v{version}' does not exist.");

        var validation = ValidateMachineDefinitionModel(definition);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Cannot activate machine definition '{definition.DefinitionId}': {string.Join("; ", validation.Errors)}");
        }

        var now = DateTime.UtcNow;
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var deactivateCommand = connection.CreateCommand())
        {
            deactivateCommand.Transaction = tx;
            deactivateCommand.CommandText = """
                UPDATE RPThemeMachineDefinitions
                SET IsActive = 0,
                    UpdatedUtc = $updatedUtc
                WHERE ThemeId = $themeId
                  AND MachineKey = $machineKey;
                """;
            deactivateCommand.Parameters.AddWithValue("$updatedUtc", now.ToString("O"));
            deactivateCommand.Parameters.AddWithValue("$themeId", themeId);
            deactivateCommand.Parameters.AddWithValue("$machineKey", machineKey);
            await deactivateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var activateCommand = connection.CreateCommand())
        {
            activateCommand.Transaction = tx;
            activateCommand.CommandText = """
                UPDATE RPThemeMachineDefinitions
                SET IsActive = 1,
                    UpdatedUtc = $updatedUtc
                WHERE DefinitionId = $definitionId;
                """;
            activateCommand.Parameters.AddWithValue("$updatedUtc", now.ToString("O"));
            activateCommand.Parameters.AddWithValue("$definitionId", definition.DefinitionId);
            await activateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Theme machine definition activated: ThemeId={ThemeId} MachineKey={MachineKey} Version={Version} DefinitionId={DefinitionId} ActorId={ActorId}",
            themeId,
            machineKey,
            version,
            definition.DefinitionId,
            actorId);
    }

    public async Task<MachineDefinitionValidationResult> ValidateMachineDefinitionAsync(string definitionId, CancellationToken cancellationToken = default)
    {
        definitionId = (definitionId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(definitionId))
        {
            throw new ArgumentException("DefinitionId is required to validate machine definition.", nameof(definitionId));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);
        var definition = await GetMachineDefinitionCoreAsync(connection, definitionId, cancellationToken);
        if (definition is null)
        {
            return new MachineDefinitionValidationResult
            {
                IsValid = false,
                Errors = [$"Machine definition '{definitionId}' does not exist."]
            };
        }

        return ValidateMachineDefinitionModel(definition);
    }

    public async Task MigrateSessionMachineVersionAsync(string sessionId, string themeId, string machineKey, int targetVersion, string actorId, CancellationToken cancellationToken = default)
    {
        sessionId = (sessionId ?? string.Empty).Trim();
        themeId = (themeId ?? string.Empty).Trim();
        machineKey = (machineKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(sessionId)
            || string.IsNullOrWhiteSpace(themeId)
            || string.IsNullOrWhiteSpace(machineKey)
            || targetVersion <= 0)
        {
            throw new ArgumentException("SessionId, themeId, machineKey, and targetVersion are required for machine migration.");
        }

        await EnsureMachineMutationAuthorizedAsync(sessionId, actorId, "migrate", cancellationToken);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureThemeMachineTablesAsync(connection, cancellationToken);
        await EnsureAdaptiveThemeMachineSnapshotColumnAsync(connection, cancellationToken);
        await EnsureThemeMachineDiagnosticsTableAsync(connection, cancellationToken);

        var targetDefinition = await GetMachineDefinitionByThemeKeyVersionAsync(connection, themeId, machineKey, targetVersion, cancellationToken)
            ?? throw new InvalidOperationException($"Cannot migrate session '{sessionId}': target machine definition '{themeId}/{machineKey}/v{targetVersion}' does not exist.");

        var validation = ValidateMachineDefinitionModel(targetDefinition);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Cannot migrate session '{sessionId}' to definition '{targetDefinition.DefinitionId}': {string.Join("; ", validation.Errors)}");
        }

        string? snapshotJson;
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.CommandText = "SELECT ThemeMachineSnapshotJson FROM RolePlayV2AdaptiveStates WHERE SessionId = $sessionId";
            selectCommand.Parameters.AddWithValue("$sessionId", sessionId);
            var snapshotObj = await selectCommand.ExecuteScalarAsync(cancellationToken);
            if (snapshotObj is null)
            {
                throw new InvalidOperationException($"Cannot migrate session '{sessionId}': adaptive state row is missing.");
            }

            snapshotJson = snapshotObj == DBNull.Value ? null : Convert.ToString(snapshotObj);
        }

        if (string.IsNullOrWhiteSpace(snapshotJson))
        {
            throw new InvalidOperationException($"Cannot migrate session '{sessionId}': machine snapshot payload is missing.");
        }

        var snapshot = DeserializeThemeMachineSnapshot(snapshotJson, sessionId);
        if (!string.Equals(snapshot.ThemeId, themeId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snapshot.MachineKey, machineKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot migrate session '{sessionId}': snapshot scope '{snapshot.ThemeId}/{snapshot.MachineKey}' does not match requested '{themeId}/{machineKey}'.");
        }

        if (!targetDefinition.States.Any(x => string.Equals(x.StateCode, snapshot.CurrentStateCode, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Cannot migrate session '{sessionId}': current snapshot state '{snapshot.CurrentStateCode}' does not exist in target definition '{targetDefinition.DefinitionId}'.");
        }

        snapshot.DefinitionId = targetDefinition.DefinitionId;
        snapshot.DefinitionVersion = targetDefinition.Version;
        snapshot.LastTransitionReasonCode = "ThemeMachineVersionMigrated";
        snapshot.LastEvaluatedUtc = DateTime.UtcNow;

        var migratedSnapshotJson = JsonSerializer.Serialize(snapshot);

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var updateSnapshotCommand = connection.CreateCommand())
        {
            updateSnapshotCommand.Transaction = tx;
            updateSnapshotCommand.CommandText = """
                UPDATE RolePlayV2AdaptiveStates
                SET ThemeMachineSnapshotJson = $snapshotJson,
                    UpdatedUtc = $updatedUtc
                WHERE SessionId = $sessionId;
                """;
            updateSnapshotCommand.Parameters.AddWithValue("$snapshotJson", migratedSnapshotJson);
            updateSnapshotCommand.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
            updateSnapshotCommand.Parameters.AddWithValue("$sessionId", sessionId);
            await updateSnapshotCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var diagnosticCommand = connection.CreateCommand())
        {
            diagnosticCommand.Transaction = tx;
            diagnosticCommand.CommandText = """
                INSERT INTO RolePlayV2ThemeMachineDiagnostics (
                    EventId, SessionId, ThemeId, MachineKey, DefinitionVersion, EventType,
                    FromStateCode, ToStateCode, TransitionId, ReasonCode, PayloadJson, OccurredUtc)
                VALUES (
                    $eventId, $sessionId, $themeId, $machineKey, $definitionVersion, $eventType,
                    $fromStateCode, $toStateCode, $transitionId, $reasonCode, $payloadJson, $occurredUtc);
                """;
            diagnosticCommand.Parameters.AddWithValue("$eventId", Guid.NewGuid().ToString("N"));
            diagnosticCommand.Parameters.AddWithValue("$sessionId", sessionId);
            diagnosticCommand.Parameters.AddWithValue("$themeId", themeId);
            diagnosticCommand.Parameters.AddWithValue("$machineKey", machineKey);
            diagnosticCommand.Parameters.AddWithValue("$definitionVersion", targetDefinition.Version);
            diagnosticCommand.Parameters.AddWithValue("$eventType", "migrate");
            diagnosticCommand.Parameters.AddWithValue("$fromStateCode", snapshot.CurrentStateCode);
            diagnosticCommand.Parameters.AddWithValue("$toStateCode", snapshot.CurrentStateCode);
            diagnosticCommand.Parameters.AddWithValue("$transitionId", DBNull.Value);
            diagnosticCommand.Parameters.AddWithValue("$reasonCode", "ThemeMachineVersionMigrated");
            diagnosticCommand.Parameters.AddWithValue("$payloadJson", JsonSerializer.Serialize(new
            {
                targetDefinitionId = targetDefinition.DefinitionId,
                targetVersion = targetDefinition.Version,
                actorId
            }));
            diagnosticCommand.Parameters.AddWithValue("$occurredUtc", DateTime.UtcNow.ToString("O"));
            await diagnosticCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Theme machine session migration completed: SessionId={SessionId} ThemeId={ThemeId} MachineKey={MachineKey} TargetVersion={TargetVersion} DefinitionId={DefinitionId} ActorId={ActorId}",
            sessionId,
            themeId,
            machineKey,
            targetVersion,
            targetDefinition.DefinitionId,
            actorId);
    }

    public async Task<RPThemeProfileThemeAssignment> SaveProfileAssignmentAsync(RPThemeProfileThemeAssignment assignment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        assignment.Id = string.IsNullOrWhiteSpace(assignment.Id) ? Guid.NewGuid().ToString("N") : assignment.Id.Trim();
        assignment.ProfileId = (assignment.ProfileId ?? string.Empty).Trim();
        assignment.ThemeId = (assignment.ThemeId ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(assignment.ProfileId) || string.IsNullOrWhiteSpace(assignment.ThemeId))
        {
            throw new ArgumentException("ProfileId and ThemeId are required for assignment.", nameof(assignment));
        }

        assignment.Weight = assignment.Weight <= 0m
            ? GetDefaultWeightForTier(assignment.Tier)
            : Math.Clamp(assignment.Weight, 0m, 1m);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfileThemeAssignments (Id, ProfileId, ThemeId, Tier, Weight, SortOrder, IsEnabled)
            VALUES ($id, $profileId, $themeId, $tier, $weight, $sortOrder, $isEnabled)
            ON CONFLICT(Id) DO UPDATE SET
                ProfileId = excluded.ProfileId,
                ThemeId = excluded.ThemeId,
                Tier = excluded.Tier,
                Weight = excluded.Weight,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled;
            """;
        command.Parameters.AddWithValue("$id", assignment.Id);
        command.Parameters.AddWithValue("$profileId", assignment.ProfileId);
        command.Parameters.AddWithValue("$themeId", assignment.ThemeId);
        command.Parameters.AddWithValue("$tier", assignment.Tier.ToString());
        command.Parameters.AddWithValue("$weight", assignment.Weight);
        command.Parameters.AddWithValue("$sortOrder", assignment.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", assignment.IsEnabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return assignment;
    }

    public async Task<IReadOnlyList<RPThemeProfileThemeAssignment>> ListProfileAssignmentsAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var assignments = new List<RPThemeProfileThemeAssignment>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, ThemeId, Tier, Weight, SortOrder, IsEnabled FROM RPThemeProfileThemeAssignments WHERE ProfileId = $profileId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assignments.Add(new RPThemeProfileThemeAssignment
            {
                Id = reader.GetString(0),
                ProfileId = reader.GetString(1),
                ThemeId = reader.GetString(2),
                Tier = Enum.TryParse<RPThemeTier>(reader.GetString(3), out var tier) ? tier : RPThemeTier.Neutral,
                Weight = reader.GetDecimal(4),
                SortOrder = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) == 1
            });
        }

        return assignments;
    }

    private static decimal GetDefaultWeightForTier(RPThemeTier tier)
        => tier switch
        {
            RPThemeTier.MustHave => 1.0m,
            RPThemeTier.StronglyPrefer => 0.8m,
            RPThemeTier.NiceToHave => 0.6m,
            RPThemeTier.Neutral => 0.5m,
            RPThemeTier.Discouraged => 0.2m,
            RPThemeTier.HardDealBreaker => 0m,
            _ => 0.5m
        };

    public async Task<bool> DeleteProfileAssignmentAsync(string assignmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPThemeProfileThemeAssignments WHERE Id = $id";
        command.Parameters.AddWithValue("$id", assignmentId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id.Trim();
        row.ProfileId = string.Empty;
        row.EscalationTier = string.IsNullOrWhiteSpace(row.EscalationTier) ? "Low" : row.EscalationTier.Trim();
        row.PrimaryLocations = NormalizeLocationList(row.PrimaryLocations);
        row.SecondaryLocations = NormalizeLocationList(row.SecondaryLocations);
        row.ExcludedLocations = NormalizeLocationList(row.ExcludedLocations);
        row.WifeReceptivity = (row.WifeReceptivity ?? string.Empty).Trim();
        row.WifeBehaviorModifier = (row.WifeBehaviorModifier ?? string.Empty).Trim();
        row.OtherManBehaviorModifier = (row.OtherManBehaviorModifier ?? string.Empty).Trim();
        row.TransitionInstruction = (row.TransitionInstruction ?? string.Empty).Trim();
        row.UpdatedUtc = DateTime.UtcNow;
        if (row.CreatedUtc == default)
        {
            row.CreatedUtc = row.UpdatedUtc;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishingMoveMatrixRows (
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand, EscalationTier,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $otherManDominanceBand, $escalationTier,
                $primaryLocationsJson, $secondaryLocationsJson, $excludedLocationsJson,
                $wifeReceptivity, $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
                EscalationTier = excluded.EscalationTier,
                PrimaryLocationsJson = excluded.PrimaryLocationsJson,
                SecondaryLocationsJson = excluded.SecondaryLocationsJson,
                ExcludedLocationsJson = excluded.ExcludedLocationsJson,
                WifeReceptivity = excluded.WifeReceptivity,
                WifeBehaviorModifier = excluded.WifeBehaviorModifier,
                OtherManBehaviorModifier = excluded.OtherManBehaviorModifier,
                TransitionInstruction = excluded.TransitionInstruction,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$desireBand", row.DesireBand);
        command.Parameters.AddWithValue("$selfRespectBand", row.SelfRespectBand);
        command.Parameters.AddWithValue("$otherManDominanceBand", row.OtherManDominanceBand);
        command.Parameters.AddWithValue("$escalationTier", row.EscalationTier);
        command.Parameters.AddWithValue("$primaryLocationsJson", SerializeStringList(row.PrimaryLocations));
        command.Parameters.AddWithValue("$secondaryLocationsJson", SerializeStringList(row.SecondaryLocations));
        command.Parameters.AddWithValue("$excludedLocationsJson", SerializeStringList(row.ExcludedLocations));
        command.Parameters.AddWithValue("$wifeReceptivity", row.WifeReceptivity);
        command.Parameters.AddWithValue("$wifeBehaviorModifier", row.WifeBehaviorModifier);
        command.Parameters.AddWithValue("$otherManBehaviorModifier", row.OtherManBehaviorModifier);
        command.Parameters.AddWithValue("$transitionInstruction", row.TransitionInstruction);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", row.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", row.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return row;
    }

    public async Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishingMoveMatrixRow>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand, EscalationTier,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPFinishingMoveMatrixRows
            ORDER BY SortOrder, Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishingMoveMatrixRow
            {
                Id = reader.GetString(0),
                DesireBand = reader.GetString(1),
                SelfRespectBand = reader.GetString(2),
                OtherManDominanceBand = reader.GetString(3),
                EscalationTier = reader.GetString(4),
                PrimaryLocations = DeserializeStringList(reader.GetString(5)),
                SecondaryLocations = DeserializeStringList(reader.GetString(6)),
                ExcludedLocations = DeserializeStringList(reader.GetString(7)),
                WifeReceptivity = reader.GetString(8),
                WifeBehaviorModifier = reader.GetString(9),
                OtherManBehaviorModifier = reader.GetString(10),
                TransitionInstruction = reader.GetString(11),
                SortOrder = reader.GetInt32(12),
                IsEnabled = reader.GetInt32(13) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(14), out var createdUtc) ? createdUtc : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(15), out var updatedUtc) ? updatedUtc : DateTime.UtcNow
            });
        }

        return rows;
    }

    public async Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
    {
        var normalizedRowId = (rowId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRowId))
        {
            return false;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishingMoveMatrixRows WHERE Id = $id";
        command.Parameters.AddWithValue("$id", normalizedRowId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    // ── Position Catalog ────────────────────────────────────────────────────

    public async Task<RPPosition> SavePositionAsync(RPPosition entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPPositions (
                Id, Name, ShortDescription, DetailedDescription,
                EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $shortDescription, $detailedDescription,
                $escalationTier, $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                ShortDescription = excluded.ShortDescription,
                DetailedDescription = excluded.DetailedDescription,
                EscalationTier = excluded.EscalationTier,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$shortDescription", entry.ShortDescription ?? string.Empty);
        command.Parameters.AddWithValue("$detailedDescription", entry.DetailedDescription ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPPosition {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public Task<IReadOnlyList<RPPosition>> ListPositionsAsync(CancellationToken cancellationToken = default)
        => ListPositionsAsync(includeDisabled: false, cancellationToken);

    public async Task<IReadOnlyList<RPPosition>> ListPositionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPPosition>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, ShortDescription, DetailedDescription, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPPositions ORDER BY SortOrder, Id"
            : "SELECT Id, Name, ShortDescription, DetailedDescription, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPPositions WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPPosition
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                ShortDescription = reader.GetString(2),
                DetailedDescription = reader.GetString(3),
                EscalationTier = reader.IsDBNull(4) ? "Low" : reader.GetString(4),
                SortOrder = reader.GetInt32(5),
                IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeletePositionAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPPositions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPPosition {Id}.", id);
        return deleted;
    }

    // ── Finishing Move Catalog ──────────────────────────────────────────────

    public async Task<RPFinishLocation> SaveFinishLocationAsync(RPFinishLocation entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishLocations (
                Id, Name, Description, Category,
                EscalationTier,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $category,
                $escalationTier,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Category = excluded.Category,
                EscalationTier = excluded.EscalationTier,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$category", entry.Category ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPFinishLocation {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public async Task<IReadOnlyList<RPFinishLocation>> ListFinishLocationsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishLocation>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, Description, Category, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishLocations ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, Category, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishLocations WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishLocation
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                Category = reader.GetString(3), EscalationTier = reader.GetString(4),
                SortOrder = reader.GetInt32(5), IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeleteFinishLocationAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishLocations WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPFinishLocation {Id}.", id);
        return deleted;
    }

    public async Task<RPFinishFacialType> SaveFinishFacialTypeAsync(RPFinishFacialType entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishFacialTypes (
                Id, Name, Description, PhysicalCues,
                EscalationTier,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $physicalCues,
                $escalationTier,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                PhysicalCues = excluded.PhysicalCues,
                EscalationTier = excluded.EscalationTier,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$physicalCues", entry.PhysicalCues ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPFinishFacialType {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public async Task<IReadOnlyList<RPFinishFacialType>> ListFinishFacialTypesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishFacialType>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, Description, PhysicalCues, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishFacialTypes ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, PhysicalCues, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishFacialTypes WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishFacialType
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                PhysicalCues = reader.GetString(3), EscalationTier = reader.GetString(4),
                SortOrder = reader.GetInt32(5), IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeleteFinishFacialTypeAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishFacialTypes WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPFinishFacialType {Id}.", id);
        return deleted;
    }

    public async Task<RPFinishReceptivityLevel> SaveFinishReceptivityLevelAsync(RPFinishReceptivityLevel entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishReceptivityLevels (
                Id, Name, Description, PhysicalCues, NarrativeCue,
                EscalationTier, EligibleDesireBands, EligibleSelfRespectBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $physicalCues, $narrativeCue,
                $escalationTier, $eligibleDesireBands, $eligibleSelfRespectBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                PhysicalCues = excluded.PhysicalCues,
                NarrativeCue = excluded.NarrativeCue,
                EscalationTier = excluded.EscalationTier,
                EligibleDesireBands = excluded.EligibleDesireBands,
                EligibleSelfRespectBands = excluded.EligibleSelfRespectBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$physicalCues", entry.PhysicalCues ?? string.Empty);
        command.Parameters.AddWithValue("$narrativeCue", entry.NarrativeCue ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$eligibleDesireBands", entry.EligibleDesireBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleSelfRespectBands", entry.EligibleSelfRespectBands ?? string.Empty);
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPFinishReceptivityLevel {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public async Task<IReadOnlyList<RPFinishReceptivityLevel>> ListFinishReceptivityLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishReceptivityLevel>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, Description, PhysicalCues, NarrativeCue, EscalationTier, EligibleDesireBands, EligibleSelfRespectBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishReceptivityLevels ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, PhysicalCues, NarrativeCue, EscalationTier, EligibleDesireBands, EligibleSelfRespectBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishReceptivityLevels WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishReceptivityLevel
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                PhysicalCues = reader.GetString(3), NarrativeCue = reader.GetString(4),
                EscalationTier = reader.GetString(5),
                EligibleDesireBands = reader.GetString(6), EligibleSelfRespectBands = reader.GetString(7),
                SortOrder = reader.GetInt32(8), IsEnabled = reader.GetInt32(9) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(10), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(11), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeleteFinishReceptivityLevelAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishReceptivityLevels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPFinishReceptivityLevel {Id}.", id);
        return deleted;
    }

    public async Task<RPFinishHisControlLevel> SaveFinishHisControlLevelAsync(RPFinishHisControlLevel entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishHisControlLevels (
                Id, Name, Description, ExampleDialogue,
                EscalationTier, EligibleOtherManDominanceBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $exampleDialogue,
                $escalationTier, $eligibleOtherManDominanceBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                ExampleDialogue = excluded.ExampleDialogue,
                EscalationTier = excluded.EscalationTier,
                EligibleOtherManDominanceBands = excluded.EligibleOtherManDominanceBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$exampleDialogue", entry.ExampleDialogue ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$eligibleOtherManDominanceBands", entry.EligibleOtherManDominanceBands ?? string.Empty);
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPFinishHisControlLevel {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public async Task<IReadOnlyList<RPFinishHisControlLevel>> ListFinishHisControlLevelsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishHisControlLevel>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, Description, ExampleDialogue, EscalationTier, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishHisControlLevels ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, ExampleDialogue, EscalationTier, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishHisControlLevels WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishHisControlLevel
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                ExampleDialogue = reader.GetString(3), EscalationTier = reader.GetString(4),
                EligibleOtherManDominanceBands = reader.GetString(5),
                SortOrder = reader.GetInt32(6), IsEnabled = reader.GetInt32(7) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(8), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(9), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeleteFinishHisControlLevelAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishHisControlLevels WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPFinishHisControlLevel {Id}.", id);
        return deleted;
    }

    public async Task<RPFinishTransitionAction> SaveFinishTransitionActionAsync(RPFinishTransitionAction entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id.Trim();
        entry.Name = (entry.Name ?? string.Empty).Trim();
        entry.UpdatedUtc = DateTime.UtcNow;
        if (entry.CreatedUtc == default) entry.CreatedUtc = entry.UpdatedUtc;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishTransitionActions (
                Id, Name, Description, TransitionText,
                EscalationTier,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $transitionText,
                $escalationTier,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                TransitionText = excluded.TransitionText,
                EscalationTier = excluded.EscalationTier,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$transitionText", entry.TransitionText ?? string.Empty);
        command.Parameters.AddWithValue("$escalationTier", entry.EscalationTier ?? "Low");
        command.Parameters.AddWithValue("$sortOrder", entry.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", entry.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Saved RPFinishTransitionAction {Id} ({Name}).", entry.Id, entry.Name);
        return entry;
    }

    public async Task<IReadOnlyList<RPFinishTransitionAction>> ListFinishTransitionActionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var rows = new List<RPFinishTransitionAction>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Name, Description, TransitionText, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishTransitionActions ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, TransitionText, EscalationTier, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishTransitionActions WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishTransitionAction
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                TransitionText = reader.GetString(3), EscalationTier = reader.GetString(4),
                SortOrder = reader.GetInt32(5), IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var u) ? u : DateTime.UtcNow
            });
        }
        return rows;
    }

    public async Task<bool> DeleteFinishTransitionActionAsync(string entryId, CancellationToken cancellationToken = default)
    {
        var id = (entryId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(id)) return false;
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPFinishTransitionActions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken) > 0;
        if (deleted) _logger.LogInformation("Deleted RPFinishTransitionAction {Id}.", id);
        return deleted;
    }

    public async Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        using var document = JsonDocument.Parse(json);
        var sourceItems = ResolveImportArray(document.RootElement);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (replaceExisting)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM RPFinishingMoveMatrixRows";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var importedCount = 0;
        foreach (var item in sourceItems)
        {
            var desireBand = GetRequiredString(item, "desireBand", "desire");
            var selfRespectBand = GetRequiredString(item, "selfRespectBand", "selfRespect");
            var otherManDominanceBand = GetRequiredString(item, "otherManDominanceBand", "dominanceBand", "dominance");

            // Derive EscalationTier from the import source (new format has it directly; old format has band ranges)
            var escalationTier = GetString(item, "escalationTier")
                ?? DeriveTierFromBandStrings(desireBand, otherManDominanceBand);

            var row = new RPFinishingMoveMatrixRow
            {
                Id = GetString(item, "id") ?? Guid.NewGuid().ToString("N"),
                DesireBand = desireBand ?? string.Empty,
                SelfRespectBand = selfRespectBand ?? string.Empty,
                OtherManDominanceBand = otherManDominanceBand ?? string.Empty,
                EscalationTier = escalationTier,
                PrimaryLocations = GetStringList(item, "primaryLocations", "locationsPrimary"),
                SecondaryLocations = GetStringList(item, "secondaryLocations", "locationsSecondary"),
                ExcludedLocations = GetStringList(item, "excludedLocations", "locationsExcluded"),
                WifeReceptivity = GetString(item, "wifeReceptivity") ?? string.Empty,
                WifeBehaviorModifier = GetString(item, "wifeBehaviorModifier", "wifeBehavior") ?? string.Empty,
                OtherManBehaviorModifier = GetString(item, "otherManBehaviorModifier", "otherManBehavior") ?? string.Empty,
                TransitionInstruction = GetString(item, "transitionInstruction", "transition", "transitionNote") ?? string.Empty,
                SortOrder = GetInt(item, "sortOrder") ?? importedCount,
                IsEnabled = GetBool(item, "isEnabled") ?? true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await SaveFinishingMoveRowWithConnectionAsync(connection, tx, row, cancellationToken);
            importedCount++;
        }

        await tx.CommitAsync(cancellationToken);
        return importedCount;
    }

    public async Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.Id = string.IsNullOrWhiteSpace(row.Id) ? Guid.NewGuid().ToString("N") : row.Id.Trim();
        row.ProfileId = string.Empty;
        row.DesireBand = (row.DesireBand ?? string.Empty).Trim();
        row.SelfRespectBand = (row.SelfRespectBand ?? string.Empty).Trim();
        row.WifeDominanceBand = (row.WifeDominanceBand ?? string.Empty).Trim();
        row.OtherManDominanceBand = (row.OtherManDominanceBand ?? string.Empty).Trim();
        row.PrimaryPositions = NormalizeLocationList(row.PrimaryPositions);
        row.SecondaryPositions = NormalizeLocationList(row.SecondaryPositions);
        row.ExcludedPositions = NormalizeLocationList(row.ExcludedPositions);
        row.WifeBehaviorModifier = (row.WifeBehaviorModifier ?? string.Empty).Trim();
        row.OtherManBehaviorModifier = (row.OtherManBehaviorModifier ?? string.Empty).Trim();
        row.TransitionInstruction = (row.TransitionInstruction ?? string.Empty).Trim();
        row.UpdatedUtc = DateTime.UtcNow;
        if (row.CreatedUtc == default)
        {
            row.CreatedUtc = row.UpdatedUtc;
        }

        if (string.IsNullOrWhiteSpace(row.DesireBand)
            || string.IsNullOrWhiteSpace(row.SelfRespectBand)
            || string.IsNullOrWhiteSpace(row.WifeDominanceBand)
            || string.IsNullOrWhiteSpace(row.OtherManDominanceBand))
        {
            throw new ArgumentException("DesireBand, SelfRespectBand, WifeDominanceBand, and OtherManDominanceBand are required.", nameof(row));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPSteerPositionMatrixRows (
                Id, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand,
                PrimaryPositionsJson, SecondaryPositionsJson, ExcludedPositionsJson,
                WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $wifeDominanceBand, $otherManDominanceBand,
                $primaryPositionsJson, $secondaryPositionsJson, $excludedPositionsJson,
                $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                WifeDominanceBand = excluded.WifeDominanceBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
                PrimaryPositionsJson = excluded.PrimaryPositionsJson,
                SecondaryPositionsJson = excluded.SecondaryPositionsJson,
                ExcludedPositionsJson = excluded.ExcludedPositionsJson,
                WifeBehaviorModifier = excluded.WifeBehaviorModifier,
                OtherManBehaviorModifier = excluded.OtherManBehaviorModifier,
                TransitionInstruction = excluded.TransitionInstruction,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$desireBand", row.DesireBand);
        command.Parameters.AddWithValue("$selfRespectBand", row.SelfRespectBand);
        command.Parameters.AddWithValue("$wifeDominanceBand", row.WifeDominanceBand);
        command.Parameters.AddWithValue("$otherManDominanceBand", row.OtherManDominanceBand);
        command.Parameters.AddWithValue("$primaryPositionsJson", SerializeStringList(row.PrimaryPositions));
        command.Parameters.AddWithValue("$secondaryPositionsJson", SerializeStringList(row.SecondaryPositions));
        command.Parameters.AddWithValue("$excludedPositionsJson", SerializeStringList(row.ExcludedPositions));
        command.Parameters.AddWithValue("$wifeBehaviorModifier", row.WifeBehaviorModifier);
        command.Parameters.AddWithValue("$otherManBehaviorModifier", row.OtherManBehaviorModifier);
        command.Parameters.AddWithValue("$transitionInstruction", row.TransitionInstruction);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", row.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", row.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);

        return row;
    }

    public async Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default)
    {
        var rows = new List<RPSteerPositionMatrixRow>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand,
                PrimaryPositionsJson, SecondaryPositionsJson, ExcludedPositionsJson,
                WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPSteerPositionMatrixRows
            ORDER BY SortOrder, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand, Id;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPSteerPositionMatrixRow
            {
                Id = reader.GetString(0),
                DesireBand = reader.GetString(1),
                SelfRespectBand = reader.GetString(2),
                WifeDominanceBand = reader.GetString(3),
                OtherManDominanceBand = reader.GetString(4),
                PrimaryPositions = DeserializeStringList(reader.GetString(5)),
                SecondaryPositions = DeserializeStringList(reader.GetString(6)),
                ExcludedPositions = DeserializeStringList(reader.GetString(7)),
                WifeBehaviorModifier = reader.GetString(8),
                OtherManBehaviorModifier = reader.GetString(9),
                TransitionInstruction = reader.GetString(10),
                SortOrder = reader.GetInt32(11),
                IsEnabled = reader.GetInt32(12) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(13), out var createdUtc) ? createdUtc : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(14), out var updatedUtc) ? updatedUtc : DateTime.UtcNow
            });
        }

        return rows;
    }

    public async Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
    {
        var normalizedRowId = (rowId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedRowId))
        {
            return false;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RPSteerPositionMatrixRows WHERE Id = $id";
        command.Parameters.AddWithValue("$id", normalizedRowId);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        using var document = JsonDocument.Parse(json);
        var sourceItems = ResolveImportArray(document.RootElement);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (replaceExisting)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM RPSteerPositionMatrixRows";
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        var importedCount = 0;
        foreach (var item in sourceItems)
        {
            var desireBand = GetRequiredString(item, "desireBand", "desire");
            var selfRespectBand = GetRequiredString(item, "selfRespectBand", "selfRespect");
            var wifeDominanceBand = GetRequiredString(item, "wifeDominanceBand", "wifeDominance", "dominanceWife");
            var otherManDominanceBand = GetRequiredString(item, "otherManDominanceBand", "otherManDominance", "dominanceOtherMan", "dominance");
            if (string.IsNullOrWhiteSpace(desireBand)
                || string.IsNullOrWhiteSpace(selfRespectBand)
                || string.IsNullOrWhiteSpace(wifeDominanceBand)
                || string.IsNullOrWhiteSpace(otherManDominanceBand))
            {
                continue;
            }

            var row = new RPSteerPositionMatrixRow
            {
                Id = GetString(item, "id") ?? Guid.NewGuid().ToString("N"),
                DesireBand = desireBand,
                SelfRespectBand = selfRespectBand,
                WifeDominanceBand = wifeDominanceBand,
                OtherManDominanceBand = otherManDominanceBand,
                PrimaryPositions = GetStringList(item, "primaryPositions", "positionsPrimary", "primaryLocations"),
                SecondaryPositions = GetStringList(item, "secondaryPositions", "positionsSecondary", "secondaryLocations"),
                ExcludedPositions = GetStringList(item, "excludedPositions", "positionsExcluded", "excludedLocations"),
                WifeBehaviorModifier = GetString(item, "wifeBehaviorModifier", "wifeBehavior") ?? string.Empty,
                OtherManBehaviorModifier = GetString(item, "otherManBehaviorModifier", "otherManBehavior") ?? string.Empty,
                TransitionInstruction = GetString(item, "transitionInstruction", "transition", "transitionNote") ?? string.Empty,
                SortOrder = GetInt(item, "sortOrder") ?? importedCount,
                IsEnabled = GetBool(item, "isEnabled") ?? true,
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };

            await SaveSteerPositionRowWithConnectionAsync(connection, tx, row, cancellationToken);
            importedCount++;
        }

        await tx.CommitAsync(cancellationToken);
        return importedCount;
    }

    public async Task<IReadOnlyList<RPThemeImportResult>> ImportFromMarkdownAsync(
        IReadOnlyList<RPThemeImportFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return [];
        }

        var runId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTime.UtcNow;
        var results = new List<RPThemeImportResult>();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await EnsureGlobalThemeLibraryProfileAsync(connection, cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await EnsureImportRunPlaceholderAsync(connection, tx, runId, startedUtc, cancellationToken);

            foreach (var file in files)
            {
                var warnings = new List<string>();
                try
                {
                    var parsed = ParseMarkdown(file.MarkdownContent, file.SourcePath, warnings);
                    var theme = new RPTheme
                    {
                        Id = parsed.Id,
                        ParentThemeId = parsed.ParentThemeId,
                        Label = parsed.Label,
                        Category = parsed.Category,
                        Weight = parsed.Weight,
                        Description = parsed.Description,
                        IsEnabled = true,
                        Keywords = parsed.Keywords.Select((kw, idx) => new RPThemeKeyword
                        {
                            ThemeId = parsed.Id,
                            GroupName = kw.Group,
                            Keyword = kw.Value,
                            SortOrder = idx
                        }).ToList(),
                        StatAffinities = parsed.StatAffinities.Select(x => new RPThemeStatAffinity
                        {
                            ThemeId = parsed.Id,
                            StatName = x.StatName,
                            Value = x.Value,
                            Rationale = x.Rationale
                        }).ToList(),
                        PhaseGuidance = parsed.PhaseGuidance.Select(x => new RPThemePhaseGuidance
                        {
                            ThemeId = parsed.Id,
                            Phase = x.Phase,
                            GuidanceText = x.Text
                        }).ToList(),
                        FitRules = parsed.FitRules.ToList(),
                        AIGenerationNotes = parsed.AIGuidanceNotes.ToList()
                    };

                    await SaveThemeWithConnectionAsync(connection, tx, theme, cancellationToken);
                    await SaveImportIssueBatchAsync(connection, tx, runId, file.SourcePath, "Warning", warnings, cancellationToken);

                    results.Add(new RPThemeImportResult
                    {
                        SourcePath = file.SourcePath,
                        ThemeId = parsed.Id,
                        Imported = true,
                        Warnings = warnings
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "RP theme import failed for source {SourcePath} in run {RunId}.", file.SourcePath, runId);
                    await SaveImportIssueAsync(connection, tx, runId, file.SourcePath, "Error", ex.Message, cancellationToken);
                    results.Add(new RPThemeImportResult
                    {
                        SourcePath = file.SourcePath,
                        Imported = false,
                        Error = ex.Message,
                        Warnings = warnings
                    });
                }
            }

            await SaveImportRunAsync(connection, tx, runId, startedUtc, results, cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RP theme markdown import run {RunId} failed and was rolled back.", runId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return results;
    }

    public async Task<IReadOnlyList<RPThemeImportResult>> SyncFromMarkdownDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = (directoryPath ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("A markdown directory path is required.", nameof(directoryPath));
        }

        var resolvedPath = ResolveSyncDirectoryPath(normalizedPath);
        if (resolvedPath is null)
        {
            throw new DirectoryNotFoundException($"Markdown directory not found: {normalizedPath}");
        }

        var files = Directory
            .GetFiles(resolvedPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(path => new RPThemeImportFile(
                Path.GetRelativePath(resolvedPath, path).Replace('\\', '/'),
                File.ReadAllText(path)))
            .ToList();

        if (files.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("Starting RP theme markdown sync from {DirectoryPath} with {FileCount} file(s).", resolvedPath, files.Count);
        IReadOnlyList<RPThemeImportResult> results;
        try
        {
            results = await ImportFromMarkdownAsync(files, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RP theme markdown sync from {DirectoryPath} failed before completion.", resolvedPath);
            throw;
        }

        _logger.LogInformation(
            "Completed RP theme markdown sync from {DirectoryPath}: imported={ImportedCount}, failed={FailedCount}.",
            resolvedPath,
            results.Count(x => x.Imported),
            results.Count(x => !x.Imported));

        return results;
    }

    private static string? ResolveSyncDirectoryPath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath) && Directory.Exists(configuredPath))
        {
            return configuredPath;
        }

        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.GetFullPath(Path.Combine(current.FullName, configuredPath));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    public async Task TruncateRolePlayAndScenarioDataAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var statements = new[]
        {
            "DELETE FROM RolePlayDebugEvents",
            "DELETE FROM RolePlayV2AdaptiveStates",
            "DELETE FROM RolePlayV2CandidateEvaluations",
            "DELETE FROM RolePlayV2PhaseTransitions",
            "DELETE FROM RolePlayV2CompletionMetadata",
            "DELETE FROM RolePlayV2DecisionOptions",
            "DELETE FROM RolePlayV2DecisionPoints",
            "DELETE FROM RolePlayV2ConceptInjections",
            "DELETE FROM RolePlayV2FormulaVersionRefs",
            "DELETE FROM RolePlayV2UnsupportedSessionErrors",
            "DELETE FROM Scenarios",
            "DELETE FROM Sessions WHERE SessionType = 'roleplay'"
        };

        foreach (var sql in statements)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        _logger.LogWarning("Development truncation completed for RP + Scenario runtime data.");
    }

    private static async Task SaveThemeWithConnectionAsync(SqliteConnection connection, SqliteTransaction tx, RPTheme theme, CancellationToken cancellationToken)
    {
        EnsureCanonicalStatAffinities(theme);
        theme.UpdatedUtc = DateTime.UtcNow;
        if (theme.CreatedUtc == default)
        {
            theme.CreatedUtc = theme.UpdatedUtc;
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            var hasLegacyProfileIdColumn = false;
            await using (var schemaCommand = connection.CreateCommand())
            {
                schemaCommand.CommandText = "PRAGMA table_info('RPThemes');";
                await using var schemaReader = await schemaCommand.ExecuteReaderAsync(cancellationToken);
                while (await schemaReader.ReadAsync(cancellationToken))
                {
                    if (string.Equals(schemaReader.GetString(1), "ProfileId", StringComparison.OrdinalIgnoreCase))
                    {
                        hasLegacyProfileIdColumn = true;
                        break;
                    }
                }
            }
            command.CommandText = hasLegacyProfileIdColumn
                ? """
                  INSERT INTO RPThemes (Id, ProfileId, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                  VALUES ($id, $profileId, $narrativeGateProfileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                  ON CONFLICT(Id) DO UPDATE SET
                      ProfileId = excluded.ProfileId,
                      NarrativeGateProfileId = excluded.NarrativeGateProfileId,
                      Label = excluded.Label,
                      Description = excluded.Description,
                      Category = excluded.Category,
                      Weight = excluded.Weight,
                      IsEnabled = excluded.IsEnabled,
                      UpdatedUtc = excluded.UpdatedUtc;
                  """
                : """
                  INSERT INTO RPThemes (Id, NarrativeGateProfileId, Label, Description, Category, Weight, IsEnabled, CreatedUtc, UpdatedUtc)
                  VALUES ($id, $narrativeGateProfileId, $label, $description, $category, $weight, $isEnabled, $createdUtc, $updatedUtc)
                  ON CONFLICT(Id) DO UPDATE SET
                      NarrativeGateProfileId = excluded.NarrativeGateProfileId,
                      Label = excluded.Label,
                      Description = excluded.Description,
                      Category = excluded.Category,
                      Weight = excluded.Weight,
                      IsEnabled = excluded.IsEnabled,
                      UpdatedUtc = excluded.UpdatedUtc;
                  """;
            command.Parameters.AddWithValue("$id", theme.Id);
            if (hasLegacyProfileIdColumn)
            {
                command.Parameters.AddWithValue("$profileId", IRPThemeService.GlobalThemeLibraryProfileId);
            }
            command.Parameters.AddWithValue("$narrativeGateProfileId", (object?)theme.NarrativeGateProfileId ?? DBNull.Value);
            command.Parameters.AddWithValue("$label", theme.Label);
            command.Parameters.AddWithValue("$description", theme.Description);
            command.Parameters.AddWithValue("$category", theme.Category);
            command.Parameters.AddWithValue("$weight", theme.Weight);
            command.Parameters.AddWithValue("$isEnabled", theme.IsEnabled ? 1 : 0);
            command.Parameters.AddWithValue("$createdUtc", theme.CreatedUtc.ToString("O"));
            command.Parameters.AddWithValue("$updatedUtc", theme.UpdatedUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);

        await using (var deleteHierarchy = connection.CreateCommand())
        {
            deleteHierarchy.Transaction = tx;
            deleteHierarchy.CommandText = "DELETE FROM RPThemeRelationships WHERE ChildThemeId = $themeId";
            deleteHierarchy.Parameters.AddWithValue("$themeId", theme.Id);
            await deleteHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(theme.ParentThemeId))
        {
            await using var insertHierarchy = connection.CreateCommand();
            insertHierarchy.Transaction = tx;
            insertHierarchy.CommandText = "INSERT INTO RPThemeRelationships (ParentThemeId, ChildThemeId, SortOrder) VALUES ($parent, $child, 0)";
            insertHierarchy.Parameters.AddWithValue("$parent", theme.ParentThemeId);
            insertHierarchy.Parameters.AddWithValue("$child", theme.Id);
            await insertHierarchy.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceThemeChildrenAsync(SqliteConnection connection, SqliteTransaction tx, RPTheme theme, CancellationToken cancellationToken)
    {
        var clearTables = new[]
        {
            "DELETE FROM RPThemeKeywords WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeStatAffinities WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeStatDecayOverrides WHERE ThemeId = $themeId",
            "DELETE FROM RPThemePhaseGuidance WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeGuidancePoints WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeAIGuidanceNotes WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeSemanticEventMappings WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeSemanticStatMappings WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeFitRules WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeNarrativeGateRules WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeSuccessorLinks WHERE SourceThemeId = $themeId"
        };

        foreach (var clearSql in clearTables)
        {
            await using var clear = connection.CreateCommand();
            clear.Transaction = tx;
            clear.CommandText = clearSql;
            clear.Parameters.AddWithValue("$themeId", theme.Id);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var keyword in theme.Keywords)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeKeywords (Id, ThemeId, GroupName, Keyword, SortOrder) VALUES ($id, $themeId, $groupName, $keyword, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(keyword.Id) ? Guid.NewGuid().ToString("N") : keyword.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$groupName", keyword.GroupName ?? string.Empty);
            cmd.Parameters.AddWithValue("$keyword", keyword.Keyword);
            cmd.Parameters.AddWithValue("$sortOrder", keyword.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var affinity in theme.StatAffinities)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeStatAffinities (Id, ThemeId, StatName, Value, Rationale) VALUES ($id, $themeId, $statName, $value, $rationale)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(affinity.Id) ? Guid.NewGuid().ToString("N") : affinity.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$statName", affinity.StatName);
            cmd.Parameters.AddWithValue("$value", affinity.Value);
            cmd.Parameters.AddWithValue("$rationale", affinity.Rationale ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var guidance in theme.PhaseGuidance)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemePhaseGuidance (Id, ThemeId, Phase, GuidanceText, DirectiveText) VALUES ($id, $themeId, $phase, $guidanceText, $directiveText)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(guidance.Id) ? Guid.NewGuid().ToString("N") : guidance.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$phase", guidance.Phase.ToString());
            cmd.Parameters.AddWithValue("$guidanceText", guidance.GuidanceText);
            cmd.Parameters.AddWithValue("$directiveText", guidance.DirectiveText ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var point in theme.GuidancePoints)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeGuidancePoints (Id, ThemeId, Phase, PointType, Text, SortOrder) VALUES ($id, $themeId, $phase, $pointType, $text, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(point.Id) ? Guid.NewGuid().ToString("N") : point.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$phase", point.Phase.ToString());
            cmd.Parameters.AddWithValue("$pointType", point.PointType.ToString());
            cmd.Parameters.AddWithValue("$text", point.Text);
            cmd.Parameters.AddWithValue("$sortOrder", point.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var rule in theme.FitRules)
        {
            var ruleId = string.IsNullOrWhiteSpace(rule.Id) ? Guid.NewGuid().ToString("N") : rule.Id;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO RPThemeFitRules (Id, ThemeId, RoleName, RoleWeight) VALUES ($id, $themeId, $roleName, $roleWeight)";
                cmd.Parameters.AddWithValue("$id", ruleId);
                cmd.Parameters.AddWithValue("$themeId", theme.Id);
                cmd.Parameters.AddWithValue("$roleName", rule.RoleName);
                cmd.Parameters.AddWithValue("$roleWeight", rule.RoleWeight);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var clause in rule.Clauses)
            {
                await using var clauseCmd = connection.CreateCommand();
                clauseCmd.Transaction = tx;
                clauseCmd.CommandText = "INSERT INTO RPThemeFitRuleClauses (Id, FitRuleId, StatName, Comparator, Threshold, PenaltyWeight, Description) VALUES ($id, $fitRuleId, $statName, $comparator, $threshold, $penaltyWeight, $description)";
                clauseCmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(clause.Id) ? Guid.NewGuid().ToString("N") : clause.Id);
                clauseCmd.Parameters.AddWithValue("$fitRuleId", ruleId);
                clauseCmd.Parameters.AddWithValue("$statName", clause.StatName);
                clauseCmd.Parameters.AddWithValue("$comparator", clause.Comparator);
                clauseCmd.Parameters.AddWithValue("$threshold", clause.Threshold);
                clauseCmd.Parameters.AddWithValue("$penaltyWeight", clause.PenaltyWeight);
                clauseCmd.Parameters.AddWithValue("$description", clause.Description ?? string.Empty);
                await clauseCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var note in theme.AIGenerationNotes)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeAIGuidanceNotes (Id, ThemeId, Section, Text, SortOrder) VALUES ($id, $themeId, $section, $text, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(note.Id) ? Guid.NewGuid().ToString("N") : note.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$section", note.Section.ToString());
            cmd.Parameters.AddWithValue("$text", note.Text);
            cmd.Parameters.AddWithValue("$sortOrder", note.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var mapping in theme.SemanticEventMappings.OrderBy(x => x.SortOrder))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeSemanticEventMappings (Id, ThemeId, EventId, Direction, Delta, ConfidenceMin, ConfidenceMax, ReasonCode, AttributionKey, SortOrder) VALUES ($id, $themeId, $eventId, $direction, $delta, $confidenceMin, $confidenceMax, $reasonCode, $attributionKey, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(mapping.Id) ? Guid.NewGuid().ToString("N") : mapping.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$eventId", mapping.EventId);
            cmd.Parameters.AddWithValue("$direction", mapping.Direction);
            cmd.Parameters.AddWithValue("$delta", (double)mapping.Delta);
            cmd.Parameters.AddWithValue("$confidenceMin", (double)mapping.ConfidenceMin);
            cmd.Parameters.AddWithValue("$confidenceMax", (double)mapping.ConfidenceMax);
            cmd.Parameters.AddWithValue("$reasonCode", mapping.ReasonCode);
            cmd.Parameters.AddWithValue("$attributionKey", mapping.AttributionKey ?? string.Empty);
            cmd.Parameters.AddWithValue("$sortOrder", mapping.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var mapping in theme.SemanticStatMappings.OrderBy(x => x.SortOrder))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeSemanticStatMappings (Id, ThemeId, EventId, TargetStat, Direction, Delta, ConfidenceMin, ConfidenceMax, ReasonCode, AttributionKey, SortOrder) VALUES ($id, $themeId, $eventId, $targetStat, $direction, $delta, $confidenceMin, $confidenceMax, $reasonCode, $attributionKey, $sortOrder)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(mapping.Id) ? Guid.NewGuid().ToString("N") : mapping.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$eventId", mapping.EventId);
            cmd.Parameters.AddWithValue("$targetStat", mapping.TargetStat);
            cmd.Parameters.AddWithValue("$direction", mapping.Direction);
            cmd.Parameters.AddWithValue("$delta", (double)mapping.Delta);
            cmd.Parameters.AddWithValue("$confidenceMin", (double)mapping.ConfidenceMin);
            cmd.Parameters.AddWithValue("$confidenceMax", (double)mapping.ConfidenceMax);
            cmd.Parameters.AddWithValue("$reasonCode", mapping.ReasonCode);
            cmd.Parameters.AddWithValue("$attributionKey", mapping.AttributionKey ?? string.Empty);
            cmd.Parameters.AddWithValue("$sortOrder", mapping.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var rule in theme.NarrativeGateRules.Select((item, index) => (Rule: item, SortOrder: index + 1)))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeNarrativeGateRules (Id, ThemeId, SortOrder, FromPhase, ToPhase, MetricKey, Comparator, Threshold) VALUES ($id, $themeId, $sortOrder, $fromPhase, $toPhase, $metricKey, $comparator, $threshold)";
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$sortOrder", rule.SortOrder);
            cmd.Parameters.AddWithValue("$fromPhase", rule.Rule.FromPhase);
            cmd.Parameters.AddWithValue("$toPhase", rule.Rule.ToPhase);
            cmd.Parameters.AddWithValue("$metricKey", rule.Rule.MetricKey);
            cmd.Parameters.AddWithValue("$comparator", rule.Rule.Comparator);
            cmd.Parameters.AddWithValue("$threshold", rule.Rule.Threshold);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var decayOverride in theme.StatDecayOverrides)
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeStatDecayOverrides (Id, ThemeId, StatName, DecayScale, Description) VALUES ($id, $themeId, $statName, $decayScale, $description)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(decayOverride.Id) ? Guid.NewGuid().ToString("N") : decayOverride.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$statName", decayOverride.StatName);
            cmd.Parameters.AddWithValue("$decayScale", (double)Math.Clamp(decayOverride.DecayScale, 0m, 1m));
            cmd.Parameters.AddWithValue("$description", decayOverride.Description ?? string.Empty);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var successor in theme.SuccessorThemeLinks.OrderBy(x => x.SortOrder))
        {
            await using var cmd = connection.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO RPThemeSuccessorLinks (SourceThemeId, SuccessorThemeId, ScoreBoost, SortOrder) VALUES ($sourceThemeId, $successorThemeId, $scoreBoost, $sortOrder)";
            cmd.Parameters.AddWithValue("$sourceThemeId", theme.Id);
            cmd.Parameters.AddWithValue("$successorThemeId", successor.SuccessorThemeId);
            cmd.Parameters.AddWithValue("$scoreBoost", (double)successor.ScoreBoost);
            cmd.Parameters.AddWithValue("$sortOrder", successor.SortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static ParsedThemeDefinition ParseMarkdown(string markdown, string sourcePath, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Markdown content is empty.");
        }

        var id = MatchValue(markdown, IdPattern()) ?? ToKebabCase(Path.GetFileNameWithoutExtension(sourcePath));
        var label = MatchValue(markdown, LabelPattern()) ?? Path.GetFileNameWithoutExtension(sourcePath);
        var category = MatchValue(markdown, CategoryPattern()) ?? "Uncategorized";
        var description = ExtractDescription(markdown);
        var parentThemeId = MatchValue(markdown, VariantPattern());

        var weightText = MatchValue(markdown, WeightPattern());
        var weight = int.TryParse(weightText, out var parsedWeight) ? Math.Clamp(parsedWeight, 1, 10) : 1;
        if (weightText is null)
        {
            warnings.Add("Missing Weight metadata; defaulted to 1.");
        }

        var keywords = ExtractKeywords(markdown);
        if (keywords.Count == 0)
        {
            warnings.Add("No keywords were detected in Keywords section.");
        }

        var statAffinities = ExtractStatAffinities(markdown);
        if (statAffinities.Count == 0)
        {
            warnings.Add("No stat affinities detected in Stat Affinities section.");
        }

        var phaseGuidance = ExtractPhaseGuidance(markdown);
        if (phaseGuidance.Count == 0)
        {
            warnings.Add("No phase guidance sections detected.");
        }

        var fitRules = ExtractFitRules(markdown, out var fitNotes, out var fitFormula);
        if (fitRules.Count == 0)
        {
            warnings.Add("No character fit logic thresholds detected.");
        }

        var aiGuidanceNotes = ExtractAIGenerationNotes(markdown);
        aiGuidanceNotes.AddRange(fitNotes);
        if (!string.IsNullOrWhiteSpace(fitFormula))
        {
            aiGuidanceNotes.Add(new RPThemeAIGuidanceNote
            {
                ThemeId = id,
                Section = RPThemeAIGuidanceSection.FitFormula,
                Text = fitFormula,
                SortOrder = aiGuidanceNotes.Count
            });
        }

        for (var i = 0; i < aiGuidanceNotes.Count; i++)
        {
            aiGuidanceNotes[i].ThemeId = id;
            aiGuidanceNotes[i].SortOrder = i;
        }

        return new ParsedThemeDefinition(id, label, category, description, weight, parentThemeId, keywords, statAffinities, phaseGuidance, fitRules, aiGuidanceNotes);
    }

    private static string? MatchValue(string content, Regex regex)
    {
        var match = regex.Match(content);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string ToKebabCase(string input)
    {
        var normalized = Regex.Replace(input.Trim(), "[^A-Za-z0-9]+", "-");
        normalized = Regex.Replace(normalized, "-+", "-").Trim('-');
        return normalized.ToLowerInvariant();
    }

    private static string ExtractDescription(string markdown)
    {
        var start = markdown.IndexOf("## Description", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => !x.StartsWith("##", StringComparison.Ordinal))
            .Where(x => !x.StartsWith("**", StringComparison.Ordinal))
            .Take(3)
            .ToList();

        return string.Join(' ', lines);
    }

    private static List<(string Group, string Value)> ExtractKeywords(string markdown)
    {
        var keywords = new List<(string Group, string Value)>();
        var start = markdown.IndexOf("## Keywords", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return keywords;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        var currentGroup = "General";
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("**") && line.EndsWith(":**", StringComparison.Ordinal))
            {
                currentGroup = line.Trim('*', ':', ' ');
                continue;
            }

            if (!line.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var values = line.TrimStart('-', ' ')
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var value in values)
            {
                keywords.Add((currentGroup, value.Trim()));
            }
        }

        return keywords;
    }

    private static List<(string StatName, int Value, string Rationale)> ExtractStatAffinities(string markdown)
    {
        var affinities = new List<(string StatName, int Value, string Rationale)>();
        var start = markdown.IndexOf("## Stat Affinities", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return affinities;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var block = nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];

        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("|", StringComparison.Ordinal) || !line.EndsWith("|", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Contains("Stat", StringComparison.OrdinalIgnoreCase) || line.Contains("---", StringComparison.Ordinal))
            {
                continue;
            }

            var cells = line.Trim('|')
                .Split('|', StringSplitOptions.None)
                .Select(x => x.Trim())
                .ToList();

            if (cells.Count < 3)
            {
                continue;
            }

            var statName = cells[0].Trim('*', ' ');
            var valueText = cells[1].Replace("+", string.Empty, StringComparison.Ordinal).Trim();
            if (string.IsNullOrWhiteSpace(statName) || !int.TryParse(valueText, out var value))
            {
                continue;
            }

            affinities.Add((statName, Math.Clamp(value, -5, 5), cells[2]));
        }

        return affinities;
    }

    private static List<(NarrativePhase Phase, string Text)> ExtractPhaseGuidance(string markdown)
    {
        var list = new List<(NarrativePhase Phase, string Text)>();
        var phaseMap = new (string Header, NarrativePhase Phase)[]
        {
            ("### Build-Up Phase", NarrativePhase.BuildUp),
            ("### Committed Phase", NarrativePhase.Committed),
            ("### Approaching Phase", NarrativePhase.Approaching),
            ("### Climax Phase", NarrativePhase.Climax),
            ("### Reset Phase", NarrativePhase.Reset)
        };

        foreach (var (header, phase) in phaseMap)
        {
            var start = markdown.IndexOf(header, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            var nextPhase = markdown.IndexOf("\n### ", start + 1, StringComparison.Ordinal);
            var nextHeader2 = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
            var end = int.MaxValue;
            if (nextPhase > start)
            {
                end = Math.Min(end, nextPhase);
            }

            if (nextHeader2 > start)
            {
                end = Math.Min(end, nextHeader2);
            }

            var block = end != int.MaxValue
                ? markdown[start..end]
                : markdown[start..];

            var text = string.Join(' ', block.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !x.StartsWith("###", StringComparison.Ordinal))
                .Where(x => !x.StartsWith("---", StringComparison.Ordinal))
                .ToList());

            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Add((phase, text));
            }
        }

        return list;
    }

    private static List<RPThemeAIGuidanceNote> ExtractAIGenerationNotes(string markdown)
    {
        var notes = new List<RPThemeAIGuidanceNote>();
        var block = GetSectionBlock(markdown, "## Notes for AI Generation");
        if (string.IsNullOrWhiteSpace(block))
        {
            return notes;
        }

        var sectionMap = new Dictionary<string, RPThemeAIGuidanceSection>(StringComparer.OrdinalIgnoreCase)
        {
            ["Key Scenario Elements to Emphasize"] = RPThemeAIGuidanceSection.KeyScenarioElement,
            ["What to Avoid"] = RPThemeAIGuidanceSection.Avoidance,
            ["Interaction Dynamics"] = RPThemeAIGuidanceSection.InteractionDynamics,
            ["Scenario Distinction from Related Themes"] = RPThemeAIGuidanceSection.ScenarioDistinction,
            ["Variations Within This Scenario"] = RPThemeAIGuidanceSection.Variation,
            ["Optional Variations Within This Scenario"] = RPThemeAIGuidanceSection.Variation,
            ["Hard Constraints"] = RPThemeAIGuidanceSection.HardConstraint,
            ["Hard Constraint"] = RPThemeAIGuidanceSection.HardConstraint
        };

        RPThemeAIGuidanceSection? currentSection = null;
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith(":**", StringComparison.Ordinal))
            {
                var sectionLabel = line.Trim('*', ':', ' ');
                currentSection = sectionMap.TryGetValue(sectionLabel, out var mapped) ? mapped : null;
                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            var noteText = NormalizeListItem(line);
            if (string.IsNullOrWhiteSpace(noteText))
            {
                continue;
            }

            notes.Add(new RPThemeAIGuidanceNote
            {
                Section = currentSection.Value,
                Text = noteText,
                SortOrder = notes.Count
            });
        }

        return notes;
    }

    private static List<RPThemeFitRule> ExtractFitRules(
        string markdown,
        out List<RPThemeAIGuidanceNote> fitNotes,
        out string fitFormula)
    {
        fitNotes = new List<RPThemeAIGuidanceNote>();
        fitFormula = string.Empty;

        var rules = new List<RPThemeFitRule>();
        var block = GetSectionBlock(markdown, "## Character State Fit Logic");
        if (string.IsNullOrWhiteSpace(block))
        {
            return rules;
        }

        RPThemeFitRule? currentRule = null;
        var currentNoteSection = RPThemeAIGuidanceSection.FitNote;
        foreach (var rawLine in block.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("##", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("**For the ", StringComparison.OrdinalIgnoreCase) && line.EndsWith(":**", StringComparison.Ordinal))
            {
                var roleName = line.Trim('*', ':', ' ');
                currentRule = new RPThemeFitRule
                {
                    RoleName = roleName,
                    RoleWeight = 1.0
                };
                rules.Add(currentRule);
                continue;
            }

            if (line.StartsWith("**Enhanced Fit", StringComparison.OrdinalIgnoreCase) && line.EndsWith(":**", StringComparison.Ordinal))
            {
                currentRule = null;
                currentNoteSection = RPThemeAIGuidanceSection.FitPattern;
                continue;
            }

            if (line.StartsWith("**Fit Score Formula:**", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            var thresholdMatch = ThresholdBulletPattern().Match(line);
            if (thresholdMatch.Success && currentRule is not null)
            {
                var statName = thresholdMatch.Groups["stat"].Value.Trim();
                var comparator = NormalizeComparator(thresholdMatch.Groups["comparator"].Value);
                var thresholdText = thresholdMatch.Groups["threshold"].Value.Trim();
                var description = thresholdMatch.Groups["description"].Value.Trim();

                if (double.TryParse(thresholdText, out var threshold) && !string.IsNullOrWhiteSpace(statName))
                {
                    currentRule.Clauses.Add(new RPThemeFitRuleClause
                    {
                        StatName = statName,
                        Comparator = comparator,
                        Threshold = threshold,
                        PenaltyWeight = 1.0,
                        Description = description
                    });
                    continue;
                }
            }

            if (line.StartsWith("-", StringComparison.Ordinal))
            {
                var noteText = NormalizeListItem(line);
                if (!string.IsNullOrWhiteSpace(noteText))
                {
                    fitNotes.Add(new RPThemeAIGuidanceNote
                    {
                        Section = currentNoteSection,
                        Text = noteText,
                        SortOrder = fitNotes.Count
                    });
                }
            }
        }

        if (string.IsNullOrWhiteSpace(fitFormula))
        {
            var formulaMatch = FitFormulaPattern().Match(block);
            if (formulaMatch.Success)
            {
                fitFormula = string.Join(' ', formulaMatch.Groups["formula"].Value
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim()));
            }
        }

        return rules.Where(x => x.Clauses.Count > 0).ToList();
    }

    private static string NormalizeListItem(string line)
    {
        var normalized = line.Trim();
        normalized = Regex.Replace(normalized, @"^[-*]\s+", string.Empty);
        normalized = Regex.Replace(normalized, @"^\d+\.\s+", string.Empty);
        return normalized.Trim();
    }

    private static string NormalizeComparator(string value)
        => value.Trim() switch
        {
            "≥" => ">=",
            "≤" => "<=",
            _ => value.Trim()
        };

    private static string GetSectionBlock(string markdown, string sectionHeader)
    {
        var start = markdown.IndexOf(sectionHeader, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        var nextHeader = markdown.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return nextHeader > start
            ? markdown[start..nextHeader]
            : markdown[start..];
    }

    private static async Task SaveImportRunAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        DateTime startedUtc,
        IReadOnlyList<RPThemeImportResult> results,
        CancellationToken cancellationToken)
    {
        var warningCount = results.Sum(x => x.Warnings.Count);
        var errorCount = results.Count(x => !x.Imported);

        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportRuns (Id, StartedUtc, CompletedUtc, ImportedCount, WarningCount, ErrorCount)
            VALUES ($id, $startedUtc, $completedUtc, $importedCount, $warningCount, $errorCount)
            ON CONFLICT(Id) DO UPDATE SET
                CompletedUtc = excluded.CompletedUtc,
                ImportedCount = excluded.ImportedCount,
                WarningCount = excluded.WarningCount,
                ErrorCount = excluded.ErrorCount;
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$importedCount", results.Count(x => x.Imported));
        command.Parameters.AddWithValue("$warningCount", warningCount);
        command.Parameters.AddWithValue("$errorCount", errorCount);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureImportRunPlaceholderAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        DateTime startedUtc,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportRuns (Id, StartedUtc, CompletedUtc, ImportedCount, WarningCount, ErrorCount)
            VALUES ($id, $startedUtc, $completedUtc, 0, 0, 0)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$startedUtc", startedUtc.ToString("O"));
        command.Parameters.AddWithValue("$completedUtc", startedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveImportIssueBatchAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        string sourcePath,
        string severity,
        IReadOnlyList<string> messages,
        CancellationToken cancellationToken)
    {
        foreach (var message in messages)
        {
            await SaveImportIssueAsync(connection, tx, runId, sourcePath, severity, message, cancellationToken);
        }
    }

    private static async Task SaveImportIssueAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string runId,
        string sourcePath,
        string severity,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPThemeImportIssues (Id, ImportRunId, SourcePath, Severity, Message)
            VALUES ($id, $importRunId, $sourcePath, $severity, $message);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$importRunId", runId);
        command.Parameters.AddWithValue("$sourcePath", sourcePath);
        command.Parameters.AddWithValue("$severity", severity);
        command.Parameters.AddWithValue("$message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveFinishingMoveRowWithConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        RPFinishingMoveMatrixRow row,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPFinishingMoveMatrixRows (
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand, EscalationTier,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $otherManDominanceBand, $escalationTier,
                $primaryLocationsJson, $secondaryLocationsJson, $excludedLocationsJson,
                $wifeReceptivity, $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
                EscalationTier = excluded.EscalationTier,
                PrimaryLocationsJson = excluded.PrimaryLocationsJson,
                SecondaryLocationsJson = excluded.SecondaryLocationsJson,
                ExcludedLocationsJson = excluded.ExcludedLocationsJson,
                WifeReceptivity = excluded.WifeReceptivity,
                WifeBehaviorModifier = excluded.WifeBehaviorModifier,
                OtherManBehaviorModifier = excluded.OtherManBehaviorModifier,
                TransitionInstruction = excluded.TransitionInstruction,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$desireBand", row.DesireBand);
        command.Parameters.AddWithValue("$selfRespectBand", row.SelfRespectBand);
        command.Parameters.AddWithValue("$otherManDominanceBand", row.OtherManDominanceBand);
        command.Parameters.AddWithValue("$escalationTier", row.EscalationTier);
        command.Parameters.AddWithValue("$primaryLocationsJson", SerializeStringList(row.PrimaryLocations));
        command.Parameters.AddWithValue("$secondaryLocationsJson", SerializeStringList(row.SecondaryLocations));
        command.Parameters.AddWithValue("$excludedLocationsJson", SerializeStringList(row.ExcludedLocations));
        command.Parameters.AddWithValue("$wifeReceptivity", row.WifeReceptivity);
        command.Parameters.AddWithValue("$wifeBehaviorModifier", row.WifeBehaviorModifier);
        command.Parameters.AddWithValue("$otherManBehaviorModifier", row.OtherManBehaviorModifier);
        command.Parameters.AddWithValue("$transitionInstruction", row.TransitionInstruction);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", row.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", row.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveSteerPositionRowWithConnectionAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        RPSteerPositionMatrixRow row,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO RPSteerPositionMatrixRows (
                Id, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand,
                PrimaryPositionsJson, SecondaryPositionsJson, ExcludedPositionsJson,
                WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $wifeDominanceBand, $otherManDominanceBand,
                $primaryPositionsJson, $secondaryPositionsJson, $excludedPositionsJson,
                $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                WifeDominanceBand = excluded.WifeDominanceBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
                PrimaryPositionsJson = excluded.PrimaryPositionsJson,
                SecondaryPositionsJson = excluded.SecondaryPositionsJson,
                ExcludedPositionsJson = excluded.ExcludedPositionsJson,
                WifeBehaviorModifier = excluded.WifeBehaviorModifier,
                OtherManBehaviorModifier = excluded.OtherManBehaviorModifier,
                TransitionInstruction = excluded.TransitionInstruction,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", row.Id);
        command.Parameters.AddWithValue("$desireBand", row.DesireBand);
        command.Parameters.AddWithValue("$selfRespectBand", row.SelfRespectBand);
        command.Parameters.AddWithValue("$wifeDominanceBand", row.WifeDominanceBand);
        command.Parameters.AddWithValue("$otherManDominanceBand", row.OtherManDominanceBand);
        command.Parameters.AddWithValue("$primaryPositionsJson", SerializeStringList(row.PrimaryPositions));
        command.Parameters.AddWithValue("$secondaryPositionsJson", SerializeStringList(row.SecondaryPositions));
        command.Parameters.AddWithValue("$excludedPositionsJson", SerializeStringList(row.ExcludedPositions));
        command.Parameters.AddWithValue("$wifeBehaviorModifier", row.WifeBehaviorModifier);
        command.Parameters.AddWithValue("$otherManBehaviorModifier", row.OtherManBehaviorModifier);
        command.Parameters.AddWithValue("$transitionInstruction", row.TransitionInstruction);
        command.Parameters.AddWithValue("$sortOrder", row.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", row.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", row.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", row.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static IReadOnlyList<JsonElement> ResolveImportArray(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray().ToList();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "rows", "matrixRows", "items", "data" })
            {
                if (TryGetPropertyIgnoreCase(root, propertyName, out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
                {
                    return rowsElement.EnumerateArray().ToList();
                }
            }
        }

        return [];
    }

    private static string SerializeStringList(IEnumerable<string> values)
        => JsonSerializer.Serialize(NormalizeLocationList(values), JsonOptions);

    private static List<string> DeserializeStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];
            return NormalizeLocationList(values);
        }
        catch
        {
            return [];
        }
    }

    private static List<string> NormalizeLocationList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return [];
        }

        return values
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetRequiredString(JsonElement element, params string[] aliases)
        => GetString(element, aliases)?.Trim();

    // Translates old band-range strings (e.g. "60-100", "30-59", "0-29") to EscalationTier.
    // Used when importing legacy JSON that predates the EscalationTier column.
    private static string DeriveTierFromBandStrings(string? desireBand, string? otherManDominanceBand)
    {
        static bool IsHigh(string? b) => b != null && (b.StartsWith("60") || b.Equals("High", StringComparison.OrdinalIgnoreCase));
        static bool IsMedium(string? b) => b != null && (b.StartsWith("30") || b.Equals("Medium", StringComparison.OrdinalIgnoreCase));
        if (IsHigh(desireBand) || IsHigh(otherManDominanceBand)) return "High";
        if (IsMedium(desireBand) || IsMedium(otherManDominanceBand)) return "Medium";
        return "Low";
    }

    private static string? GetString(JsonElement element, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (TryGetPropertyIgnoreCase(element, alias, out var value))
            {
                return value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Number => value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => null
                };
            }
        }

        return null;
    }

    private static int? GetInt(JsonElement element, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!TryGetPropertyIgnoreCase(element, alias, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static bool? GetBool(JsonElement element, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!TryGetPropertyIgnoreCase(element, alias, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static List<string> GetStringList(JsonElement element, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (!TryGetPropertyIgnoreCase(element, alias, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Array)
            {
                return NormalizeLocationList(value.EnumerateArray()
                    .Where(x => x.ValueKind == JsonValueKind.String)
                    .Select(x => x.GetString() ?? string.Empty));
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString() ?? string.Empty;
                var split = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                return NormalizeLocationList(split);
            }
        }

        return [];
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static async Task<string?> LoadParentThemeIdAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ParentThemeId FROM RPThemeRelationships WHERE ChildThemeId = $childThemeId ORDER BY SortOrder LIMIT 1";
        command.Parameters.AddWithValue("$childThemeId", themeId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value?.ToString();
    }

    private static async Task<List<RPThemeSuccessorLink>> LoadThemeSuccessorLinksAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeSuccessorLink>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT SourceThemeId, SuccessorThemeId, ScoreBoost, SortOrder FROM RPThemeSuccessorLinks WHERE SourceThemeId = $sourceThemeId ORDER BY SortOrder, SuccessorThemeId";
        command.Parameters.AddWithValue("$sourceThemeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeSuccessorLink
            {
                SourceThemeId = reader.GetString(0),
                SuccessorThemeId = reader.GetString(1),
                ScoreBoost = Math.Clamp(Convert.ToDecimal(reader.GetValue(2)), 1m, 100m),
                SortOrder = reader.GetInt32(3)
            });
        }

        return list;
    }

    private static async Task<List<RPThemeKeyword>> LoadThemeKeywordsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeKeyword>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, GroupName, Keyword, SortOrder FROM RPThemeKeywords WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeKeyword
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                GroupName = reader.GetString(2),
                Keyword = reader.GetString(3),
                SortOrder = reader.GetInt32(4)
            });
        }

        return list;
    }

    private static async Task<List<RPThemeStatAffinity>> LoadThemeStatAffinitiesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeStatAffinity>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, StatName, Value, Rationale FROM RPThemeStatAffinities WHERE ThemeId = $themeId ORDER BY StatName";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeStatAffinity
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                StatName = reader.GetString(2),
                Value = reader.GetInt32(3),
                Rationale = reader.GetString(4)
            });
        }

        return list;
    }

    private static async Task<List<RPThemePhaseGuidance>> LoadThemePhaseGuidanceAsync(SqliteConnection connection, string themeId, ILogger logger, CancellationToken cancellationToken)
    {
        var list = new List<RPThemePhaseGuidance>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, Phase, GuidanceText, DirectiveText FROM RPThemePhaseGuidance WHERE ThemeId = $themeId";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phaseRaw = reader.GetString(2);
            if (!TryParseStoredNarrativePhase(phaseRaw, out var parsedPhase))
            {
                logger.LogError("RPThemePhaseGuidance row for theme '{ThemeId}' has unrecognised Phase value '{PhaseRaw}'. Row skipped — fix the data to restore guidance for this phase.", themeId, phaseRaw);
                continue;
            }

            list.Add(new RPThemePhaseGuidance
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                Phase = parsedPhase,
                GuidanceText = reader.GetString(3),
                DirectiveText = reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
            });
        }

        return list;
    }

    private static async Task<List<RPThemeGuidancePoint>> LoadThemeGuidancePointsAsync(SqliteConnection connection, string themeId, ILogger logger, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeGuidancePoint>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, Phase, PointType, Text, SortOrder FROM RPThemeGuidancePoints WHERE ThemeId = $themeId ORDER BY SortOrder";
        command.Parameters.AddWithValue("$themeId", themeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var phaseRaw = reader.GetString(2);
            if (!TryParseStoredNarrativePhase(phaseRaw, out var parsedPhase))
            {
                logger.LogError("RPThemeGuidancePoints row for theme '{ThemeId}' has unrecognised Phase value '{PhaseRaw}'. Row skipped — fix the data to restore guidance for this phase.", themeId, phaseRaw);
                continue;
            }

            list.Add(new RPThemeGuidancePoint
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                Phase = parsedPhase,
                PointType = Enum.TryParse<RPThemeGuidancePointType>(reader.GetString(3), out var pointType)
                    ? pointType
                    : RPThemeGuidancePointType.Emphasis,
                Text = reader.GetString(4),
                SortOrder = reader.GetInt32(5)
            });
        }

        return list;
    }

    private static bool TryParseStoredNarrativePhase(string rawValue, out NarrativePhase phase)
    {
        if (Enum.TryParse<NarrativePhase>(rawValue, ignoreCase: true, out phase))
        {
            return true;
        }

        // Backward compatibility for legacy typo persisted in older records.
        if (string.Equals(rawValue, "Commited", StringComparison.OrdinalIgnoreCase))
        {
            phase = NarrativePhase.Committed;
            return true;
        }

        phase = default;
        return false;
    }

    private static async Task<List<RPThemeFitRule>> LoadThemeFitRulesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var rules = new List<RPThemeFitRule>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, ThemeId, RoleName, RoleWeight FROM RPThemeFitRules WHERE ThemeId = $themeId ORDER BY RoleName";
            command.Parameters.AddWithValue("$themeId", themeId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rules.Add(new RPThemeFitRule
                {
                    Id = reader.GetString(0),
                    ThemeId = reader.GetString(1),
                    RoleName = reader.GetString(2),
                    RoleWeight = reader.GetDouble(3)
                });
            }
        }

        foreach (var rule in rules)
        {
            await using var clauseCommand = connection.CreateCommand();
            clauseCommand.CommandText = "SELECT Id, FitRuleId, StatName, Comparator, Threshold, PenaltyWeight, Description FROM RPThemeFitRuleClauses WHERE FitRuleId = $fitRuleId ORDER BY StatName";
            clauseCommand.Parameters.AddWithValue("$fitRuleId", rule.Id);
            await using var clauseReader = await clauseCommand.ExecuteReaderAsync(cancellationToken);
            while (await clauseReader.ReadAsync(cancellationToken))
            {
                rule.Clauses.Add(new RPThemeFitRuleClause
                {
                    Id = clauseReader.GetString(0),
                    FitRuleId = clauseReader.GetString(1),
                    StatName = clauseReader.GetString(2),
                    Comparator = clauseReader.GetString(3),
                    Threshold = clauseReader.GetDouble(4),
                    PenaltyWeight = clauseReader.GetDouble(5),
                    Description = clauseReader.GetString(6)
                });
            }
        }

        return rules;
    }

    private static async Task<List<RPThemeAIGuidanceNote>> LoadThemeAIGuidanceNotesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeAIGuidanceNote>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, Section, Text, SortOrder FROM RPThemeAIGuidanceNotes WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeAIGuidanceNote
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                Section = Enum.TryParse<RPThemeAIGuidanceSection>(reader.GetString(2), out var section)
                    ? section
                    : RPThemeAIGuidanceSection.KeyScenarioElement,
                Text = reader.GetString(3),
                SortOrder = reader.GetInt32(4)
            });
        }

        return list;
    }

    private static async Task<List<RPSemanticEventMapping>> LoadThemeSemanticEventMappingsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPSemanticEventMapping>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, EventId, Direction, Delta, ConfidenceMin, ConfidenceMax, ReasonCode, AttributionKey, SortOrder FROM RPThemeSemanticEventMappings WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapping = new RPSemanticEventMapping
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                EventId = reader.GetString(2),
                Direction = reader.GetString(3),
                Delta = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture),
                ConfidenceMin = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                ConfidenceMax = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                ReasonCode = reader.GetString(7),
                AttributionKey = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                SortOrder = reader.GetInt32(9)
            };

            if (string.IsNullOrWhiteSpace(mapping.EventId)
                || string.IsNullOrWhiteSpace(mapping.Direction)
                || string.IsNullOrWhiteSpace(mapping.ReasonCode))
            {
                throw new InvalidOperationException(
                    $"Invalid semantic mapping row '{mapping.Id}' for theme '{themeId}': required fields are empty.");
            }

            if (mapping.ConfidenceMin < 0m || mapping.ConfidenceMax > 1m || mapping.ConfidenceMin > mapping.ConfidenceMax)
            {
                throw new InvalidOperationException(
                    $"Invalid semantic mapping row '{mapping.Id}' for theme '{themeId}': confidence range [{mapping.ConfidenceMin}, {mapping.ConfidenceMax}] is invalid.");
            }

            list.Add(mapping);
        }

        return list;
    }

    private static async Task<List<RPSemanticStatMapping>> LoadThemeSemanticStatMappingsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPSemanticStatMapping>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ThemeId, EventId, TargetStat, Direction, Delta, ConfidenceMin, ConfidenceMax, ReasonCode, AttributionKey, SortOrder FROM RPThemeSemanticStatMappings WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var mapping = new RPSemanticStatMapping
            {
                Id = reader.GetString(0),
                ThemeId = reader.GetString(1),
                EventId = reader.GetString(2),
                TargetStat = reader.GetString(3),
                Direction = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                Delta = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture),
                ConfidenceMin = Convert.ToDecimal(reader.GetValue(6), CultureInfo.InvariantCulture),
                ConfidenceMax = Convert.ToDecimal(reader.GetValue(7), CultureInfo.InvariantCulture),
                ReasonCode = reader.GetString(8),
                AttributionKey = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                SortOrder = reader.GetInt32(10)
            };

            mapping.Direction = string.IsNullOrWhiteSpace(mapping.Direction)
                ? "increase"
                : mapping.Direction.Trim();

            var isBlankRow = string.IsNullOrWhiteSpace(mapping.EventId)
                && string.IsNullOrWhiteSpace(mapping.TargetStat)
                && string.IsNullOrWhiteSpace(mapping.ReasonCode);

            if (isBlankRow)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(mapping.EventId)
                || string.IsNullOrWhiteSpace(mapping.TargetStat)
                || string.IsNullOrWhiteSpace(mapping.ReasonCode))
            {
                continue;
            }

            if (!SupportedSemanticStatKeys.Contains(mapping.TargetStat))
            {
                throw new InvalidOperationException(
                    $"Invalid semantic stat mapping row '{mapping.Id}' for theme '{themeId}': target stat '{mapping.TargetStat}' is not supported.");
            }

            if (mapping.ConfidenceMin < 0m || mapping.ConfidenceMax > 1m || mapping.ConfidenceMin > mapping.ConfidenceMax)
            {
                throw new InvalidOperationException(
                    $"Invalid semantic stat mapping row '{mapping.Id}' for theme '{themeId}': confidence range [{mapping.ConfidenceMin}, {mapping.ConfidenceMax}] is invalid.");
            }

            list.Add(mapping);
        }

        return list;
    }

    private static async Task<List<NarrativeGateRule>> LoadThemeNarrativeGateRulesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<NarrativeGateRule>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, SortOrder, FromPhase, ToPhase, MetricKey, Comparator, Threshold FROM RPThemeNarrativeGateRules WHERE ThemeId = $themeId ORDER BY SortOrder, Id";
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new NarrativeGateRule
            {
                SortOrder = reader.GetInt32(1),
                FromPhase = reader.GetString(2),
                ToPhase = reader.GetString(3),
                MetricKey = reader.GetString(4),
                Comparator = reader.GetString(5),
                Threshold = Convert.ToDecimal(reader.GetValue(6))
            });
        }

        return NormalizeNarrativeGateRules(list);
    }

    private static async Task<List<RPThemeStatDecayOverride>> LoadThemeStatDecayOverridesAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        var list = new List<RPThemeStatDecayOverride>();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, StatName, DecayScale, Description FROM RPThemeStatDecayOverrides WHERE ThemeId = $themeId ORDER BY StatName";
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new RPThemeStatDecayOverride
            {
                Id = reader.GetString(0),
                ThemeId = themeId,
                StatName = reader.GetString(1),
                DecayScale = Math.Clamp(Convert.ToDecimal(reader.GetValue(2)), 0m, 1m),
                Description = reader.IsDBNull(3) ? string.Empty : reader.GetString(3)
            });
        }

        return list;
    }

    private async Task EnsureThemeNarrativeGateRulesPersistedAsync(SqliteConnection connection, RPTheme theme, CancellationToken cancellationToken)
    {
        if (theme.NarrativeGateRules.Count > 0)
        {
            return;
        }

        var seed = await LoadDefaultNarrativeGateRulesAsync(connection, cancellationToken);
        if (seed.Count == 0)
        {
            return;
        }

        theme.NarrativeGateRules = seed;
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<List<NarrativeGateRule>> LoadDefaultNarrativeGateRulesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT RulesJson FROM NarrativeGateProfiles WHERE IsDefault = 1 LIMIT 1";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not string rulesJson || string.IsNullOrWhiteSpace(rulesJson))
        {
            return [];
        }

        var parsed = JsonSerializer.Deserialize<List<NarrativeGateRule>>(rulesJson, JsonOptions) ?? [];
        return NormalizeNarrativeGateRules(parsed);
    }

    private static List<NarrativeGateRule> NormalizeNarrativeGateRules(IReadOnlyList<NarrativeGateRule> rules)
    {
        return rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.FromPhase)
                && !string.IsNullOrWhiteSpace(rule.ToPhase)
                && !string.IsNullOrWhiteSpace(rule.MetricKey)
                && !string.IsNullOrWhiteSpace(rule.Comparator))
            .Select((rule, index) => new NarrativeGateRule
            {
                SortOrder = index + 1,
                FromPhase = rule.FromPhase.Trim(),
                ToPhase = rule.ToPhase.Trim(),
                MetricKey = rule.MetricKey.Trim(),
                Comparator = rule.Comparator.Trim(),
                Threshold = rule.Threshold
            })
            .ToList();
    }

    private static List<RPThemeSuccessorLink> NormalizeSuccessorThemeLinks(string sourceThemeId, IReadOnlyList<RPThemeSuccessorLink> links)
    {
        return links
            .Where(link => !string.IsNullOrWhiteSpace(link.SuccessorThemeId))
            .Select((link, index) => new RPThemeSuccessorLink
            {
                SourceThemeId = sourceThemeId,
                SuccessorThemeId = link.SuccessorThemeId.Trim(),
                ScoreBoost = Math.Clamp(link.ScoreBoost, 1m, 100m),
                SortOrder = index + 1
            })
            .GroupBy(link => link.SuccessorThemeId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static async Task ValidateSuccessorThemeLinksAsync(SqliteConnection connection, RPTheme theme, CancellationToken cancellationToken)
    {
        foreach (var link in theme.SuccessorThemeLinks)
        {
            if (string.IsNullOrWhiteSpace(link.SuccessorThemeId))
            {
                throw new InvalidOperationException($"Theme '{theme.Id}' has an invalid successor link: successor theme id is required.");
            }

            if (string.Equals(link.SuccessorThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Theme '{theme.Id}' has an invalid successor link: a theme cannot point to itself.");
            }

            if (link.ScoreBoost <= 0m)
            {
                throw new InvalidOperationException($"Theme '{theme.Id}' has an invalid successor link to '{link.SuccessorThemeId}': score boost must be greater than zero.");
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM RPThemes WHERE Id = $id";
            command.Parameters.AddWithValue("$id", link.SuccessorThemeId);
            var exists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!exists)
            {
                throw new InvalidOperationException($"Theme '{theme.Id}' references missing successor theme '{link.SuccessorThemeId}'.");
            }
        }
    }

    private static void ValidateRequiredNarrativeTransitions(IReadOnlyList<NarrativeGateRule> rules)
    {
        if (rules.Count == 0)
        {
            throw new ArgumentException("Theme narrative gate values require at least one rule.", nameof(rules));
        }

        var missingTransitions = RequiredNarrativeTransitions
            .Where(required => !rules.Any(rule => string.Equals(rule.FromPhase, required.From, StringComparison.OrdinalIgnoreCase)
                && string.Equals(rule.ToPhase, required.To, StringComparison.OrdinalIgnoreCase)))
            .Select(required => $"{required.From}->{required.To}")
            .ToList();

        if (missingTransitions.Count > 0)
        {
            throw new InvalidOperationException($"Theme narrative gate values are missing required transition paths: {string.Join(", ", missingTransitions)}");
        }
    }

    private async Task EnsureCanonicalStatAffinitiesPersistedAsync(SqliteConnection connection, RPTheme theme, CancellationToken cancellationToken)
    {
        if (!EnsureCanonicalStatAffinities(theme))
        {
            return;
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await ReplaceThemeChildrenAsync(connection, tx, theme, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static bool EnsureCanonicalStatAffinities(RPTheme theme)
    {
        var changed = false;
        var existing = new Dictionary<string, RPThemeStatAffinity>(StringComparer.OrdinalIgnoreCase);
        foreach (var affinity in theme.StatAffinities)
        {
            if (string.IsNullOrWhiteSpace(affinity.StatName))
            {
                continue;
            }

            var trimmedName = affinity.StatName.Trim();
            if (!existing.TryGetValue(trimmedName, out var tracked))
            {
                affinity.StatName = trimmedName;
                affinity.Value = Math.Clamp(affinity.Value, -5, 5);
                existing[trimmedName] = affinity;
                continue;
            }

            tracked.Value = Math.Clamp(tracked.Value + affinity.Value, -5, 5);
            changed = true;
        }

        if (existing.Count != theme.StatAffinities.Count)
        {
            theme.StatAffinities = existing.Values.ToList();
            changed = true;
        }

        foreach (var statName in AdaptiveStatCatalog.CanonicalStatNames)
        {
            if (existing.ContainsKey(statName))
            {
                continue;
            }

            theme.StatAffinities.Add(new RPThemeStatAffinity
            {
                Id = Guid.NewGuid().ToString("N"),
                ThemeId = theme.Id,
                StatName = statName,
                Value = 0,
                Rationale = AutoBackfillRationale
            });
            changed = true;
        }

        return changed;
    }

    private static List<RPThemeMachineState> NormalizeMachineStates(
        string definitionId,
        IReadOnlyList<RPThemeMachineState>? states)
    {
        var normalized = new List<RPThemeMachineState>();
        foreach (var state in states ?? [])
        {
            var stateCode = (state.StateCode ?? string.Empty).Trim();
            var label = (state.Label ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stateCode))
            {
                throw new ArgumentException("Machine state StateCode is required.", nameof(states));
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException($"Machine state '{stateCode}' label is required.", nameof(states));
            }

            normalized.Add(new RPThemeMachineState
            {
                StateId = string.IsNullOrWhiteSpace(state.StateId) ? Guid.NewGuid().ToString("N") : state.StateId.Trim(),
                DefinitionId = definitionId,
                StateCode = stateCode,
                Label = label,
                IsInitial = state.IsInitial,
                IsTerminal = state.IsTerminal,
                SortOrder = state.SortOrder
            });
        }

        return normalized
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.StateCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<RPThemeMachineTransition> NormalizeMachineTransitions(
        string definitionId,
        IReadOnlyList<RPThemeMachineTransition>? transitions,
        DateTime now)
    {
        var normalized = new List<RPThemeMachineTransition>();
        foreach (var transition in transitions ?? [])
        {
            var fromStateCode = (transition.FromStateCode ?? string.Empty).Trim();
            var toStateCode = (transition.ToStateCode ?? string.Empty).Trim();
            var triggerType = (transition.TriggerType ?? string.Empty).Trim();
            var gateConfigJson = (transition.GateConfigJson ?? string.Empty).Trim();
            var blockReasonCode = (transition.BlockReasonCode ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fromStateCode)
                || string.IsNullOrWhiteSpace(toStateCode)
                || string.IsNullOrWhiteSpace(triggerType)
                || string.IsNullOrWhiteSpace(gateConfigJson)
                || string.IsNullOrWhiteSpace(blockReasonCode))
            {
                throw new ArgumentException("Machine transitions require FromStateCode, ToStateCode, TriggerType, GateConfigJson, and BlockReasonCode.", nameof(transitions));
            }

            normalized.Add(new RPThemeMachineTransition
            {
                TransitionId = string.IsNullOrWhiteSpace(transition.TransitionId) ? Guid.NewGuid().ToString("N") : transition.TransitionId.Trim(),
                DefinitionId = definitionId,
                FromStateCode = fromStateCode,
                ToStateCode = toStateCode,
                Priority = transition.Priority,
                TriggerType = triggerType,
                GateConfigJson = gateConfigJson,
                BlockReasonCode = blockReasonCode,
                IsEnabled = transition.IsEnabled,
                CreatedUtc = transition.CreatedUtc == default ? now : transition.CreatedUtc,
                UpdatedUtc = now
            });
        }

        return normalized
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.FromStateCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TransitionId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<RPThemeMachineDefinition>> ListMachineDefinitionsCoreAsync(
        SqliteConnection connection,
        string themeId,
        CancellationToken cancellationToken)
    {
        var definitions = new List<RPThemeMachineDefinition>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DefinitionId, ThemeId, MachineKey, Version, Name, IsActive, IsSeeded, CreatedUtc, UpdatedUtc
            FROM RPThemeMachineDefinitions
            WHERE ThemeId = $themeId
            ORDER BY MachineKey, Version DESC;
            """;
        command.Parameters.AddWithValue("$themeId", themeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var definition = new RPThemeMachineDefinition
            {
                DefinitionId = reader.GetString(0),
                ThemeId = reader.GetString(1),
                MachineKey = reader.GetString(2),
                Version = reader.GetInt32(3),
                Name = reader.GetString(4),
                IsActive = reader.GetInt32(5) == 1,
                IsSeeded = reader.GetInt32(6) == 1,
                CreatedUtc = ParseRequiredUtcTimestamp(reader.GetString(7), "RPThemeMachineDefinitions.CreatedUtc"),
                UpdatedUtc = ParseRequiredUtcTimestamp(reader.GetString(8), "RPThemeMachineDefinitions.UpdatedUtc")
            };

            definitions.Add(definition);
        }

        foreach (var definition in definitions)
        {
            definition.States = await LoadMachineStatesCoreAsync(connection, definition.DefinitionId, cancellationToken);
            definition.Transitions = await LoadMachineTransitionsCoreAsync(connection, definition.DefinitionId, cancellationToken);
        }

        return definitions;
    }

    private async Task<RPThemeMachineDefinition?> GetMachineDefinitionCoreAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DefinitionId, ThemeId, MachineKey, Version, Name, IsActive, IsSeeded, CreatedUtc, UpdatedUtc
            FROM RPThemeMachineDefinitions
            WHERE DefinitionId = $definitionId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var definition = new RPThemeMachineDefinition
        {
            DefinitionId = reader.GetString(0),
            ThemeId = reader.GetString(1),
            MachineKey = reader.GetString(2),
            Version = reader.GetInt32(3),
            Name = reader.GetString(4),
            IsActive = reader.GetInt32(5) == 1,
            IsSeeded = reader.GetInt32(6) == 1,
            CreatedUtc = ParseRequiredUtcTimestamp(reader.GetString(7), "RPThemeMachineDefinitions.CreatedUtc"),
            UpdatedUtc = ParseRequiredUtcTimestamp(reader.GetString(8), "RPThemeMachineDefinitions.UpdatedUtc")
        };

        definition.States = await LoadMachineStatesCoreAsync(connection, definition.DefinitionId, cancellationToken);
        definition.Transitions = await LoadMachineTransitionsCoreAsync(connection, definition.DefinitionId, cancellationToken);
        return definition;
    }

    private async Task<RPThemeMachineDefinition?> GetMachineDefinitionByThemeKeyVersionAsync(
        SqliteConnection connection,
        string themeId,
        string machineKey,
        int version,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DefinitionId
            FROM RPThemeMachineDefinitions
            WHERE ThemeId = $themeId
              AND MachineKey = $machineKey
              AND Version = $version
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$themeId", themeId);
        command.Parameters.AddWithValue("$machineKey", machineKey);
        command.Parameters.AddWithValue("$version", version);

        var definitionId = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
        return string.IsNullOrWhiteSpace(definitionId)
            ? null
            : await GetMachineDefinitionCoreAsync(connection, definitionId, cancellationToken);
    }

    private static async Task<List<RPThemeMachineState>> LoadMachineStatesCoreAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        var states = new List<RPThemeMachineState>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT StateId, DefinitionId, StateCode, Label, IsInitial, IsTerminal, SortOrder
            FROM RPThemeMachineStates
            WHERE DefinitionId = $definitionId
            ORDER BY SortOrder, StateCode, StateId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states.Add(new RPThemeMachineState
            {
                StateId = reader.GetString(0),
                DefinitionId = reader.GetString(1),
                StateCode = reader.GetString(2),
                Label = reader.GetString(3),
                IsInitial = reader.GetInt32(4) == 1,
                IsTerminal = reader.GetInt32(5) == 1,
                SortOrder = reader.GetInt32(6)
            });
        }

        return states;
    }

    private static async Task<List<RPThemeMachineTransition>> LoadMachineTransitionsCoreAsync(
        SqliteConnection connection,
        string definitionId,
        CancellationToken cancellationToken)
    {
        var transitions = new List<RPThemeMachineTransition>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TransitionId, DefinitionId, FromStateCode, ToStateCode, Priority, TriggerType,
                   GateConfigJson, BlockReasonCode, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPThemeMachineTransitions
            WHERE DefinitionId = $definitionId
            ORDER BY Priority, FromStateCode, TransitionId;
            """;
        command.Parameters.AddWithValue("$definitionId", definitionId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new RPThemeMachineTransition
            {
                TransitionId = reader.GetString(0),
                DefinitionId = reader.GetString(1),
                FromStateCode = reader.GetString(2),
                ToStateCode = reader.GetString(3),
                Priority = reader.GetInt32(4),
                TriggerType = reader.GetString(5),
                GateConfigJson = reader.GetString(6),
                BlockReasonCode = reader.GetString(7),
                IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = ParseRequiredUtcTimestamp(reader.GetString(9), "RPThemeMachineTransitions.CreatedUtc"),
                UpdatedUtc = ParseRequiredUtcTimestamp(reader.GetString(10), "RPThemeMachineTransitions.UpdatedUtc")
            });
        }

        return transitions;
    }

    private static DateTime ParseRequiredUtcTimestamp(string raw, string fieldName)
    {
        if (!DateTime.TryParse(raw, null, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidOperationException($"Invalid timestamp '{raw}' in {fieldName}.");
        }

        return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
    }

    private static MachineDefinitionValidationResult ValidateMachineDefinitionModel(RPThemeMachineDefinition definition)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.ThemeId))
        {
            errors.Add("ThemeId is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.MachineKey))
        {
            errors.Add("MachineKey is required.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            errors.Add("Name is required.");
        }

        if (definition.Version <= 0)
        {
            errors.Add("Version must be greater than zero.");
        }

        if (definition.States.Count == 0)
        {
            errors.Add("At least one state is required.");
        }

        var initialStateCount = definition.States.Count(x => x.IsInitial);
        if (initialStateCount != 1)
        {
            errors.Add($"Machine definition must have exactly one initial state. Found {initialStateCount}.");
        }

        var stateCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var state in definition.States)
        {
            var stateCode = (state.StateCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stateCode))
            {
                errors.Add("State code is required.");
                continue;
            }

            if (!stateCodes.Add(stateCode))
            {
                errors.Add($"Duplicate state code '{stateCode}'.");
            }

            if (string.IsNullOrWhiteSpace(state.Label))
            {
                errors.Add($"State '{stateCode}' label is required.");
            }
        }

        if (definition.Transitions.Count == 0)
        {
            warnings.Add("No transitions are configured.");
        }

        var seenTransitionPriorities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var transition in definition.Transitions)
        {
            var fromStateCode = (transition.FromStateCode ?? string.Empty).Trim();
            var toStateCode = (transition.ToStateCode ?? string.Empty).Trim();
            var triggerType = (transition.TriggerType ?? string.Empty).Trim();
            var blockReasonCode = (transition.BlockReasonCode ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(fromStateCode) || string.IsNullOrWhiteSpace(toStateCode))
            {
                errors.Add("Transition from/to state codes are required.");
                continue;
            }

            if (!stateCodes.Contains(fromStateCode))
            {
                errors.Add($"Transition '{transition.TransitionId}' references unknown source state '{fromStateCode}'.");
            }

            if (!stateCodes.Contains(toStateCode))
            {
                errors.Add($"Transition '{transition.TransitionId}' references unknown target state '{toStateCode}'.");
            }

            if (string.IsNullOrWhiteSpace(triggerType))
            {
                errors.Add($"Transition '{transition.TransitionId}' trigger type is required.");
            }

            if (string.IsNullOrWhiteSpace(blockReasonCode))
            {
                errors.Add($"Transition '{transition.TransitionId}' block reason code is required.");
            }

            if (string.IsNullOrWhiteSpace(transition.GateConfigJson))
            {
                errors.Add($"Transition '{transition.TransitionId}' gate config JSON is required.");
            }
            else
            {
                JsonElement gateRoot;
                try
                {
                    using var doc = JsonDocument.Parse(transition.GateConfigJson);
                    gateRoot = doc.RootElement.Clone();
                }
                catch (JsonException)
                {
                    errors.Add($"Transition '{transition.TransitionId}' gate config JSON is invalid.");
                    gateRoot = default;
                }

                if (gateRoot.ValueKind == JsonValueKind.Object
                    && string.Equals(triggerType, "cooldown-eligibility", StringComparison.OrdinalIgnoreCase))
                {
                    ValidateCooldownEligibilityGateConfig(errors, transition.TransitionId, gateRoot);
                }
            }

            var priorityKey = $"{fromStateCode}|{transition.Priority}";
            if (!seenTransitionPriorities.Add(priorityKey))
            {
                errors.Add($"Duplicate transition priority '{transition.Priority}' for source state '{fromStateCode}'.");
            }
        }

        return new MachineDefinitionValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static void ValidateCooldownEligibilityGateConfig(
        ICollection<string> errors,
        string transitionId,
        JsonElement gateConfig)
    {
        // Prefer the canonical post-migration turn threshold (minimumTurns); accept legacy
        // minimumInteractions (÷3 ceiling) for rows that predate the B-044 interaction→turn
        // migration (spec 001-replace-interactions-turns T025).
        int minimumTurns = -1;
        var hasCanonicalTurns = gateConfig.TryGetProperty("minimumTurns", out var minimumTurnsProperty)
            && minimumTurnsProperty.ValueKind == JsonValueKind.Number
            && minimumTurnsProperty.TryGetInt32(out minimumTurns)
            && minimumTurns >= 0;
        int legacyInteractions = -1;
        var hasLegacyInteractions = gateConfig.TryGetProperty("minimumInteractions", out var legacyInteractionsProperty)
            && legacyInteractionsProperty.ValueKind == JsonValueKind.Number
            && legacyInteractionsProperty.TryGetInt32(out legacyInteractions)
            && legacyInteractions >= 0;

        if (!hasCanonicalTurns && !hasLegacyInteractions)
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config must include integer minimumTurns >= 0.");
        }

        if (!gateConfig.TryGetProperty("requireReturnBeatCompleted", out var requireReturnBeatCompletedProperty)
            || (requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.True
                && requireReturnBeatCompletedProperty.ValueKind != JsonValueKind.False))
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config must include boolean requireReturnBeatCompleted.");
            return;
        }

        if (!requireReturnBeatCompletedProperty.GetBoolean())
        {
            return;
        }

        if (!gateConfig.TryGetProperty("returnBeatCompletionSignals", out var signalsProperty)
            || signalsProperty.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config must include string array returnBeatCompletionSignals when requireReturnBeatCompleted is true.");
            return;
        }

        var signalCount = 0;
        foreach (var element in signalsProperty.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(element.GetString()))
            {
                errors.Add($"Transition '{transitionId}' returnBeatCompletionSignals entries must be non-empty strings.");
                return;
            }

            signalCount++;
        }

        if (signalCount == 0)
        {
            errors.Add($"Transition '{transitionId}' returnBeatCompletionSignals must include at least one entry when requireReturnBeatCompleted is true.");
        }

        var transgressorRoleName = ResolveRequiredReturnBeatRoleName(
            errors,
            transitionId,
            gateConfig,
            "returnBeatTransgressorRole");
        var partnerRoleName = ResolveRequiredReturnBeatRoleName(
            errors,
            transitionId,
            gateConfig,
            "returnBeatPartnerRole");

        if (!string.IsNullOrWhiteSpace(transgressorRoleName)
            && !string.IsNullOrWhiteSpace(partnerRoleName)
            && string.Equals(transgressorRoleName, partnerRoleName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Transition '{transitionId}' returnBeatTransgressorRole and returnBeatPartnerRole must be different values.");
        }
    }

    private static string ResolveRequiredReturnBeatRoleName(
        ICollection<string> errors,
        string transitionId,
        JsonElement gateConfig,
        string propertyName)
    {
        if (!gateConfig.TryGetProperty(propertyName, out var roleProperty)
            || roleProperty.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config must include string {propertyName} when requireReturnBeatCompleted is true.");
            return string.Empty;
        }

        var rawRoleName = roleProperty.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(rawRoleName))
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config {propertyName} must be a non-empty string.");
            return string.Empty;
        }

        var normalizedRoleName = CharacterRoleCatalog.Normalize(rawRoleName);
        if (string.Equals(normalizedRoleName, CharacterRoleCatalog.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Transition '{transitionId}' cooldown gate config {propertyName} must not be Unknown.");
            return string.Empty;
        }

        return normalizedRoleName;
    }

    private async Task EnsureMachineMutationAuthorizedAsync(
        string sessionScope,
        string actorId,
        string operation,
        CancellationToken cancellationToken)
    {
        actorId = (actorId ?? string.Empty).Trim();
        operation = (operation ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new InvalidOperationException("ActorId is required for machine mutation operations.");
        }

        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new InvalidOperationException("Operation is required for machine mutation authorization.");
        }

        if (_themeMachineAuthorizationService is null)
        {
            throw new InvalidOperationException("Theme machine authorization service is required for machine mutation operations.");
        }

        var result = await _themeMachineAuthorizationService.AuthorizeMutationAsync(
            new ThemeMachineAuthorizationRequest
            {
                SessionId = sessionScope,
                ActorId = actorId,
                ActorRole = ExtractActorRoleFromActorId(actorId),
                Operation = operation
            },
            cancellationToken);

        if (!result.Authorized)
        {
            throw new UnauthorizedAccessException(result.Reason);
        }
    }

    private static string ExtractActorRoleFromActorId(string actorId)
    {
        var trimmed = actorId.Trim();
        var separator = trimmed.IndexOf(':');
        if (separator <= 0)
        {
            return trimmed;
        }

        return trimmed[..separator];
    }

    private static ThemeMachineSessionSnapshot DeserializeThemeMachineSnapshot(string payloadJson, string sessionId)
    {
        ThemeMachineSessionSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<ThemeMachineSessionSnapshot>(payloadJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Session '{sessionId}' machine snapshot JSON is invalid.", ex);
        }

        if (snapshot is null)
        {
            throw new InvalidOperationException($"Session '{sessionId}' machine snapshot JSON is null.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.ThemeId)
            || string.IsNullOrWhiteSpace(snapshot.MachineKey)
            || string.IsNullOrWhiteSpace(snapshot.DefinitionId)
            || snapshot.DefinitionVersion <= 0
            || string.IsNullOrWhiteSpace(snapshot.CurrentStateCode)
            || snapshot.TurnsInCurrentState < 0
            || snapshot.LastEvaluatedUtc == default)
        {
            throw new InvalidOperationException($"Session '{sessionId}' machine snapshot JSON is missing required fields.");
        }

        return snapshot;
    }

    private static async Task<bool> ThemeExistsAsync(SqliteConnection connection, string themeId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RPThemes WHERE Id = $themeId";
        command.Parameters.AddWithValue("$themeId", themeId);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static async Task<bool> MachineDefinitionExistsAsync(SqliteConnection connection, string definitionId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM RPThemeMachineDefinitions WHERE DefinitionId = $definitionId";
        command.Parameters.AddWithValue("$definitionId", definitionId);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static async Task EnsureThemeMachineTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RPThemeMachineDefinitions (
                DefinitionId TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                MachineKey TEXT NOT NULL,
                Version INTEGER NOT NULL,
                Name TEXT NOT NULL,
                IsActive INTEGER NOT NULL DEFAULT 0,
                IsSeeded INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE,
                UNIQUE (ThemeId, MachineKey, Version)
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeMachineDefinitions_Theme_MachineKey_Version
                ON RPThemeMachineDefinitions (ThemeId, MachineKey, Version DESC);

            CREATE TABLE IF NOT EXISTS RPThemeMachineStates (
                StateId TEXT PRIMARY KEY,
                DefinitionId TEXT NOT NULL,
                StateCode TEXT NOT NULL,
                Label TEXT NOT NULL,
                IsInitial INTEGER NOT NULL DEFAULT 0,
                IsTerminal INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (DefinitionId) REFERENCES RPThemeMachineDefinitions(DefinitionId) ON DELETE CASCADE,
                UNIQUE (DefinitionId, StateCode)
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeMachineStates_Definition_Sort
                ON RPThemeMachineStates (DefinitionId, SortOrder, StateId);

            CREATE TABLE IF NOT EXISTS RPThemeMachineTransitions (
                TransitionId TEXT PRIMARY KEY,
                DefinitionId TEXT NOT NULL,
                FromStateCode TEXT NOT NULL,
                ToStateCode TEXT NOT NULL,
                Priority INTEGER NOT NULL,
                TriggerType TEXT NOT NULL,
                GateConfigJson TEXT NOT NULL,
                BlockReasonCode TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (DefinitionId) REFERENCES RPThemeMachineDefinitions(DefinitionId) ON DELETE CASCADE,
                UNIQUE (DefinitionId, FromStateCode, Priority)
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeMachineTransitions_Definition_FromState_Priority
                ON RPThemeMachineTransitions (DefinitionId, FromStateCode, Priority);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureAdaptiveThemeMachineSnapshotColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='ThemeMachineSnapshotJson'";
        var hasColumn = Convert.ToInt64(await checkCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasColumn)
        {
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ThemeMachineSnapshotJson TEXT NULL";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureThemeMachineDiagnosticsTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RolePlayV2ThemeMachineDiagnostics (
                EventId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                ThemeId TEXT NOT NULL,
                MachineKey TEXT NOT NULL,
                DefinitionVersion INTEGER NOT NULL,
                EventType TEXT NOT NULL,
                FromStateCode TEXT NULL,
                ToStateCode TEXT NULL,
                TransitionId TEXT NULL,
                ReasonCode TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                OccurredUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RolePlayV2ThemeMachineDiagnostics_Session_OccurredUtc
                ON RolePlayV2ThemeMachineDiagnostics (SessionId, OccurredUtc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!_supplementalTablesEnsured)
        {
            await EnsureSupplementalTablesAsync(connection, cancellationToken);
            await EnsureRpThemesColumnsAsync(connection, cancellationToken);
            await EnsureRPThemeProfilesSelectionColumnAsync(connection, cancellationToken);
            _supplementalTablesEnsured = true;
        }

        return connection;
    }

    private async Task EnsureRpThemesColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await RPThemesTableHasNarrativeGateProfileIdAsync(connection, cancellationToken))
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "ALTER TABLE RPThemes ADD COLUMN NarrativeGateProfileId TEXT NULL";
        await command.ExecuteNonQueryAsync(cancellationToken);
        _rpThemesHasNarrativeGateProfileIdColumn = true;
    }

    private static async Task EnsureRPThemeProfilesSelectionColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var checkNewCommand = connection.CreateCommand();
        checkNewCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPThemeProfiles') WHERE name='ThemeSelectionTurnsPerTheme'";
        var hasNewColumn = Convert.ToInt64(await checkNewCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasNewColumn)
        {
            return;
        }

        await using var checkLegacyCommand = connection.CreateCommand();
        checkLegacyCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPThemeProfiles') WHERE name='ThemeSelectionInteractionsPerTheme'";
        var hasLegacyColumn = Convert.ToInt64(await checkLegacyCommand.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasLegacyColumn)
        {
            await using var renameCommand = connection.CreateCommand();
            renameCommand.CommandText = "ALTER TABLE RPThemeProfiles RENAME COLUMN ThemeSelectionInteractionsPerTheme TO ThemeSelectionTurnsPerTheme";
            await renameCommand.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = "ALTER TABLE RPThemeProfiles ADD COLUMN ThemeSelectionTurnsPerTheme INTEGER NOT NULL DEFAULT 2";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureSupplementalTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await MigrateLegacyMatrixTablesToGlobalAsync(connection, cancellationToken);
        await MigrateFinishingMoveMatrixToV2Async(connection, cancellationToken);
        await MigrateFinishingMoveMatrixToV3Async(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RPFinishingMoveMatrixRows (
                Id TEXT PRIMARY KEY,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                OtherManDominanceBand TEXT NOT NULL,
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                PrimaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                SecondaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                ExcludedLocationsJson TEXT NOT NULL DEFAULT '[]',
                WifeReceptivity TEXT NOT NULL DEFAULT '',
                WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                TransitionInstruction TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (EscalationTier)
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishingMoveMatrixRows_Sort
                ON RPFinishingMoveMatrixRows (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPPositions (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                ShortDescription TEXT NOT NULL DEFAULT '',
                DetailedDescription TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (Name)
            );

            CREATE INDEX IF NOT EXISTS IX_RPPositions_Sort
                ON RPPositions (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPSteerPositionMatrixRows (
                Id TEXT PRIMARY KEY,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                WifeDominanceBand TEXT NOT NULL,
                OtherManDominanceBand TEXT NOT NULL,
                PrimaryPositionsJson TEXT NOT NULL DEFAULT '[]',
                SecondaryPositionsJson TEXT NOT NULL DEFAULT '[]',
                ExcludedPositionsJson TEXT NOT NULL DEFAULT '[]',
                WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                TransitionInstruction TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand)
            );

            CREATE INDEX IF NOT EXISTS IX_RPSteerPositionMatrixRows_Sort
                ON RPSteerPositionMatrixRows (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPThemeAIGuidanceNotes (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                Section TEXT NOT NULL,
                Text TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeAIGuidanceNotes_Theme_Sort
                ON RPThemeAIGuidanceNotes (ThemeId, SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPThemeNarrativeGateRules (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                FromPhase TEXT NOT NULL,
                ToPhase TEXT NOT NULL,
                MetricKey TEXT NOT NULL,
                Comparator TEXT NOT NULL,
                Threshold REAL NOT NULL,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeNarrativeGateRules_Theme_Sort
                ON RPThemeNarrativeGateRules (ThemeId, SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPThemeStatDecayOverrides (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                StatName TEXT NOT NULL,
                DecayScale REAL NOT NULL DEFAULT 1.0,
                Description TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeStatDecayOverrides_Theme
                ON RPThemeStatDecayOverrides (ThemeId);

            CREATE TABLE IF NOT EXISTS RPThemeSuccessorLinks (
                SourceThemeId TEXT NOT NULL,
                SuccessorThemeId TEXT NOT NULL,
                ScoreBoost REAL NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (SourceThemeId, SuccessorThemeId),
                FOREIGN KEY (SourceThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE,
                FOREIGN KEY (SuccessorThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeSuccessorLinks_Source_Sort
                ON RPThemeSuccessorLinks (SourceThemeId, SortOrder, SuccessorThemeId);

            CREATE TABLE IF NOT EXISTS RPFinishLocations (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishLocations_Sort
                ON RPFinishLocations (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPFinishFacialTypes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                PhysicalCues TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishFacialTypes_Sort
                ON RPFinishFacialTypes (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPFinishReceptivityLevels (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                PhysicalCues TEXT NOT NULL DEFAULT '',
                NarrativeCue TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                EligibleDesireBands TEXT NOT NULL DEFAULT '',
                EligibleSelfRespectBands TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishReceptivityLevels_Sort
                ON RPFinishReceptivityLevels (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPFinishHisControlLevels (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                ExampleDialogue TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                EligibleOtherManDominanceBands TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishHisControlLevels_Sort
                ON RPFinishHisControlLevels (SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPFinishTransitionActions (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                TransitionText TEXT NOT NULL DEFAULT '',
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishTransitionActions_Sort
                ON RPFinishTransitionActions (SortOrder, Id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureFinishCatalogEscalationTierColumnsAsync(connection, cancellationToken);
        await EnsureFinishCatalogBandEligibilityColumnsAsync(connection, cancellationToken);
    }

    private static async Task EnsureFinishCatalogBandEligibilityColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPFinishReceptivityLevels') WHERE name='EligibleDesireBands'";
        var hasDesireBands = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDesireBands)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE RPFinishReceptivityLevels ADD COLUMN EligibleDesireBands TEXT NOT NULL DEFAULT ''";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPFinishReceptivityLevels') WHERE name='EligibleSelfRespectBands'";
        var hasSelfRespectBands = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasSelfRespectBands)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE RPFinishReceptivityLevels ADD COLUMN EligibleSelfRespectBands TEXT NOT NULL DEFAULT ''";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }

        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPFinishHisControlLevels') WHERE name='EligibleOtherManDominanceBands'";
        var hasDominanceBands = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDominanceBands)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE RPFinishHisControlLevels ADD COLUMN EligibleOtherManDominanceBands TEXT NOT NULL DEFAULT ''";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureFinishCatalogEscalationTierColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        string[] tables = ["RPFinishLocations", "RPFinishFacialTypes", "RPFinishReceptivityLevels", "RPFinishHisControlLevels", "RPFinishTransitionActions"];
        foreach (var table in tables)
        {
            var check = connection.CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='EscalationTier'";
            var hasColumn = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!hasColumn)
            {
                var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN EscalationTier TEXT NOT NULL DEFAULT 'Low'";
                await alter.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    private static async Task MigrateFinishingMoveMatrixToV2Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var needsMigration = await TableHasColumnAsync(connection, "RPFinishingMoveMatrixRows", "DominanceBand", cancellationToken);
        if (!needsMigration)
        {
            return;
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS RPFinishingMoveMatrixRows_Archived_v2 (
                Id TEXT NOT NULL,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                DominanceBand TEXT NOT NULL,
                PrimaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                SecondaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                ExcludedLocationsJson TEXT NOT NULL DEFAULT '[]',
                WifeReceptivity TEXT NOT NULL DEFAULT '',
                WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                TransitionInstruction TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                ArchivedUtc TEXT NOT NULL
            );

            INSERT INTO RPFinishingMoveMatrixRows_Archived_v2 (
                Id, DesireBand, SelfRespectBand, DominanceBand,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier,
                TransitionInstruction, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, ArchivedUtc)
            SELECT
                Id, DesireBand, SelfRespectBand, DominanceBand,
                COALESCE(PrimaryLocationsJson, '[]'), COALESCE(SecondaryLocationsJson, '[]'), COALESCE(ExcludedLocationsJson, '[]'),
                COALESCE(WifeReceptivity, ''), COALESCE(WifeBehaviorModifier, ''), COALESCE(OtherManBehaviorModifier, ''),
                COALESCE(TransitionInstruction, ''), SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, $archivedUtc
            FROM RPFinishingMoveMatrixRows;

            DROP TABLE RPFinishingMoveMatrixRows;
            DROP INDEX IF EXISTS IX_RPFinishingMoveMatrixRows_Sort;
            """;
        cmd.Parameters.AddWithValue("$archivedUtc", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// V3 migration: changes the UNIQUE constraint on RPFinishingMoveMatrixRows from
    /// (DesireBand, SelfRespectBand, OtherManDominanceBand) to (EscalationTier).
    /// The old constraint prevents seeding the 3-row tier format when all rows have empty bands.
    /// </summary>
    private static async Task MigrateFinishingMoveMatrixToV3Async(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Check whether the table exists with the old band UNIQUE constraint.
        var check = connection.CreateCommand();
        check.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='RPFinishingMoveMatrixRows'";
        var tableSql = (await check.ExecuteScalarAsync(cancellationToken)) as string;
        if (tableSql == null || !tableSql.Contains("UNIQUE (DesireBand, SelfRespectBand, OtherManDominanceBand)", StringComparison.OrdinalIgnoreCase))
        {
            return; // Already on V3 schema or table does not exist yet.
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE RPFinishingMoveMatrixRows_V3 (
                Id TEXT PRIMARY KEY,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                OtherManDominanceBand TEXT NOT NULL,
                EscalationTier TEXT NOT NULL DEFAULT 'Low',
                PrimaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                SecondaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                ExcludedLocationsJson TEXT NOT NULL DEFAULT '[]',
                WifeReceptivity TEXT NOT NULL DEFAULT '',
                WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                TransitionInstruction TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (EscalationTier)
            );

            INSERT INTO RPFinishingMoveMatrixRows_V3 (
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand, EscalationTier,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier,
                TransitionInstruction, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            SELECT
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand, EscalationTier,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier,
                TransitionInstruction, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPFinishingMoveMatrixRows;

            DROP TABLE RPFinishingMoveMatrixRows;
            DROP INDEX IF EXISTS IX_RPFinishingMoveMatrixRows_Sort;

            ALTER TABLE RPFinishingMoveMatrixRows_V3 RENAME TO RPFinishingMoveMatrixRows;

            CREATE INDEX IF NOT EXISTS IX_RPFinishingMoveMatrixRows_Sort
                ON RPFinishingMoveMatrixRows (SortOrder, Id);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private static async Task MigrateLegacyMatrixTablesToGlobalAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var finishingNeedsMigration = await TableHasColumnAsync(connection, "RPFinishingMoveMatrixRows", "ProfileId", cancellationToken);
        var steerNeedsMigration = await TableHasColumnAsync(connection, "RPSteerPositionMatrixRows", "ProfileId", cancellationToken);

        if (!finishingNeedsMigration && !steerNeedsMigration)
        {
            return;
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        if (finishingNeedsMigration)
        {
            await using var archiveCommand = connection.CreateCommand();
            archiveCommand.Transaction = tx;
            archiveCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS RPFinishingMoveMatrixRows_Archived (
                    Id TEXT NOT NULL,
                    ProfileId TEXT NOT NULL,
                    DesireBand TEXT NOT NULL,
                    SelfRespectBand TEXT NOT NULL,
                    DominanceBand TEXT NOT NULL,
                    PrimaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                    SecondaryLocationsJson TEXT NOT NULL DEFAULT '[]',
                    ExcludedLocationsJson TEXT NOT NULL DEFAULT '[]',
                    WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                    OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                    TransitionInstruction TEXT NOT NULL DEFAULT '',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    ArchivedUtc TEXT NOT NULL
                );

                INSERT INTO RPFinishingMoveMatrixRows_Archived (
                    Id, ProfileId, DesireBand, SelfRespectBand, DominanceBand,
                    PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                    WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                    SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, ArchivedUtc)
                SELECT
                    Id, ProfileId, DesireBand, SelfRespectBand, DominanceBand,
                    PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                    WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                    SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, $archivedUtc
                FROM RPFinishingMoveMatrixRows;

                DROP TABLE RPFinishingMoveMatrixRows;
                DROP INDEX IF EXISTS IX_RPFinishingMoveMatrixRows_Profile_Sort;
                DROP INDEX IF EXISTS IX_RPFinishingMoveMatrixRows_Sort;
            """;
            archiveCommand.Parameters.AddWithValue("$archivedUtc", DateTime.UtcNow.ToString("O"));
            await archiveCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        if (steerNeedsMigration)
        {
            await using var archiveCommand = connection.CreateCommand();
            archiveCommand.Transaction = tx;
            archiveCommand.CommandText = """
                CREATE TABLE IF NOT EXISTS RPSteerPositionMatrixRows_Archived (
                    Id TEXT NOT NULL,
                    ProfileId TEXT NOT NULL,
                    DesireBand TEXT NOT NULL,
                    SelfRespectBand TEXT NOT NULL,
                    WifeDominanceBand TEXT NOT NULL,
                    OtherManDominanceBand TEXT NOT NULL,
                    PrimaryPositionsJson TEXT NOT NULL DEFAULT '[]',
                    SecondaryPositionsJson TEXT NOT NULL DEFAULT '[]',
                    ExcludedPositionsJson TEXT NOT NULL DEFAULT '[]',
                    WifeBehaviorModifier TEXT NOT NULL DEFAULT '',
                    OtherManBehaviorModifier TEXT NOT NULL DEFAULT '',
                    TransitionInstruction TEXT NOT NULL DEFAULT '',
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsEnabled INTEGER NOT NULL DEFAULT 1,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    ArchivedUtc TEXT NOT NULL
                );

                INSERT INTO RPSteerPositionMatrixRows_Archived (
                    Id, ProfileId, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand,
                    PrimaryPositionsJson, SecondaryPositionsJson, ExcludedPositionsJson,
                    WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                    SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, ArchivedUtc)
                SELECT
                    Id, ProfileId, DesireBand, SelfRespectBand, WifeDominanceBand, OtherManDominanceBand,
                    PrimaryPositionsJson, SecondaryPositionsJson, ExcludedPositionsJson,
                    WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                    SortOrder, IsEnabled, CreatedUtc, UpdatedUtc, $archivedUtc
                FROM RPSteerPositionMatrixRows;

                DROP TABLE RPSteerPositionMatrixRows;
                DROP INDEX IF EXISTS IX_RPSteerPositionMatrixRows_Profile_Sort;
                DROP INDEX IF EXISTS IX_RPSteerPositionMatrixRows_Sort;
            """;
            archiveCommand.Parameters.AddWithValue("$archivedUtc", DateTime.UtcNow.ToString("O"));
            await archiveCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    private static async Task<bool> TableHasColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name = $columnName";
        command.Parameters.AddWithValue("$columnName", columnName);
        var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    private static async Task EnsureGlobalThemeLibraryProfileAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPThemeProfiles (Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, 0, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", IRPThemeService.GlobalThemeLibraryProfileId);
        command.Parameters.AddWithValue("$name", "Global Theme Library");
        command.Parameters.AddWithValue("$description", "Shared RP theme definitions used across profiles.");
        command.Parameters.AddWithValue("$createdUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<bool> RPThemesTableHasProfileIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_rpThemesHasProfileIdColumn.HasValue)
        {
            return _rpThemesHasProfileIdColumn.Value;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('RPThemes');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(1);
            if (string.Equals(columnName, "ProfileId", StringComparison.OrdinalIgnoreCase))
            {
                _rpThemesHasProfileIdColumn = true;
                return true;
            }
        }

        _rpThemesHasProfileIdColumn = false;
        return false;
    }

    private async Task<bool> RPThemesTableHasNarrativeGateProfileIdAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_rpThemesHasNarrativeGateProfileIdColumn.HasValue)
        {
            return _rpThemesHasNarrativeGateProfileIdColumn.Value;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('RPThemes');";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var columnName = reader.GetString(1);
            if (string.Equals(columnName, "NarrativeGateProfileId", StringComparison.OrdinalIgnoreCase))
            {
                _rpThemesHasNarrativeGateProfileIdColumn = true;
                return true;
            }
        }

        _rpThemesHasNarrativeGateProfileIdColumn = false;
        return false;
    }

    [GeneratedRegex(@"\*\*ID:\*\*\s*`?(?<value>[^`\r\n]+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex IdPattern();

    [GeneratedRegex(@"\*\*Label:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();

    [GeneratedRegex(@"\*\*Category:\*\*\s*(?<value>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryPattern();

    [GeneratedRegex(@"\*\*Weight:\*\*\s*(?<value>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WeightPattern();

    [GeneratedRegex(@"\*\*Variant of:\*\*\s*`?(?<value>[^`\r\n]+)`?", RegexOptions.IgnoreCase)]
    private static partial Regex VariantPattern();

    [GeneratedRegex(@"^\s*-\s*\*\*(?<stat>[A-Za-z][A-Za-z0-9]*)\s*(?<comparator>>=|<=|>|<|=|≥|≤)\s*(?<threshold>\d+(?:\.\d+)?)\:\*\*\s*(?<description>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex ThresholdBulletPattern();

    [GeneratedRegex(@"\*\*Fit Score Formula:\*\*\s*```[\r\n]+(?<formula>.*?)```", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex FitFormulaPattern();

    private sealed record ParsedThemeDefinition(
        string Id,
        string Label,
        string Category,
        string Description,
        int Weight,
        string? ParentThemeId,
        IReadOnlyList<(string Group, string Value)> Keywords,
        IReadOnlyList<(string StatName, int Value, string Rationale)> StatAffinities,
        IReadOnlyList<(NarrativePhase Phase, string Text)> PhaseGuidance,
        IReadOnlyList<RPThemeFitRule> FitRules,
        IReadOnlyList<RPThemeAIGuidanceNote> AIGuidanceNotes);
}
