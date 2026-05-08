using System.Text.RegularExpressions;
using System.Text.Json;
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
    private bool? _rpThemesHasProfileIdColumn;
    private bool? _rpThemesHasNarrativeGateProfileIdColumn;
    private bool _supplementalTablesEnsured;

    public RPThemeService(IOptions<PersistenceOptions> options, ILogger<RPThemeService> logger)
    {
        _connectionString = options.Value.ConnectionString;
        _logger = logger;
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
            INSERT INTO RPThemeProfiles (Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $isDefault, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                IsDefault = excluded.IsDefault,
                UpdatedUtc = excluded.UpdatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", profile.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return profile;
    }

    public async Task<IReadOnlyList<RPThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var profiles = new List<RPThemeProfile>();
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc FROM RPThemeProfiles ORDER BY Name";

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
                UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
            });
        }

        return profiles;
    }

    public async Task<RPThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, CreatedUtc, UpdatedUtc FROM RPThemeProfiles WHERE Id = $id";
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
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
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

        theme.NarrativeGateRules = NormalizeNarrativeGateRules(theme.NarrativeGateRules);

        EnsureCanonicalStatAffinities(theme);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        ValidateRequiredNarrativeTransitions(theme.NarrativeGateRules);
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
                    GuidanceText = guidance.GuidanceText
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
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
            theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
            theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
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
            theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
            theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
            theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
            theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
            theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
            theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
            theme.NarrativeGateRules = await LoadThemeNarrativeGateRulesAsync(connection, theme.Id, cancellationToken);
            await EnsureCanonicalStatAffinitiesPersistedAsync(connection, theme, cancellationToken);
            await EnsureThemeNarrativeGateRulesPersistedAsync(connection, theme, cancellationToken);
        }

        return themes;
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
        theme.Keywords = await LoadThemeKeywordsAsync(connection, theme.Id, cancellationToken);
        theme.StatAffinities = await LoadThemeStatAffinitiesAsync(connection, theme.Id, cancellationToken);
        theme.PhaseGuidance = await LoadThemePhaseGuidanceAsync(connection, theme.Id, _logger, cancellationToken);
        theme.GuidancePoints = await LoadThemeGuidancePointsAsync(connection, theme.Id, _logger, cancellationToken);
        theme.FitRules = await LoadThemeFitRulesAsync(connection, theme.Id, cancellationToken);
        theme.AIGenerationNotes = await LoadThemeAIGuidanceNotesAsync(connection, theme.Id, cancellationToken);
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
        row.DesireBand = (row.DesireBand ?? string.Empty).Trim();
        row.SelfRespectBand = (row.SelfRespectBand ?? string.Empty).Trim();
        row.OtherManDominanceBand = (row.OtherManDominanceBand ?? string.Empty).Trim();
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

        if (string.IsNullOrWhiteSpace(row.DesireBand) || string.IsNullOrWhiteSpace(row.SelfRespectBand) || string.IsNullOrWhiteSpace(row.OtherManDominanceBand))
        {
            throw new ArgumentException("DesireBand, SelfRespectBand, and OtherManDominanceBand are required.", nameof(row));
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RPFinishingMoveMatrixRows (
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $otherManDominanceBand,
                $primaryLocationsJson, $secondaryLocationsJson, $excludedLocationsJson,
                $wifeReceptivity, $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
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
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc
            FROM RPFinishingMoveMatrixRows
            ORDER BY SortOrder, DesireBand, SelfRespectBand, OtherManDominanceBand, Id;
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
                PrimaryLocations = DeserializeStringList(reader.GetString(4)),
                SecondaryLocations = DeserializeStringList(reader.GetString(5)),
                ExcludedLocations = DeserializeStringList(reader.GetString(6)),
                WifeReceptivity = reader.GetString(7),
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
                EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $category,
                $eligibleDesireBands, $eligibleSelfRespectBands, $eligibleOtherManDominanceBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                Category = excluded.Category,
                EligibleDesireBands = excluded.EligibleDesireBands,
                EligibleSelfRespectBands = excluded.EligibleSelfRespectBands,
                EligibleOtherManDominanceBands = excluded.EligibleOtherManDominanceBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$category", entry.Category ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleDesireBands", entry.EligibleDesireBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleSelfRespectBands", entry.EligibleSelfRespectBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleOtherManDominanceBands", entry.EligibleOtherManDominanceBands ?? string.Empty);
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
            ? "SELECT Id, Name, Description, Category, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishLocations ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, Category, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishLocations WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishLocation
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                Category = reader.GetString(3), EligibleDesireBands = reader.GetString(4),
                EligibleSelfRespectBands = reader.GetString(5), EligibleOtherManDominanceBands = reader.GetString(6),
                SortOrder = reader.GetInt32(7), IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(9), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(10), out var u) ? u : DateTime.UtcNow
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
                EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $physicalCues,
                $eligibleDesireBands, $eligibleSelfRespectBands, $eligibleOtherManDominanceBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                PhysicalCues = excluded.PhysicalCues,
                EligibleDesireBands = excluded.EligibleDesireBands,
                EligibleSelfRespectBands = excluded.EligibleSelfRespectBands,
                EligibleOtherManDominanceBands = excluded.EligibleOtherManDominanceBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$physicalCues", entry.PhysicalCues ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleDesireBands", entry.EligibleDesireBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleSelfRespectBands", entry.EligibleSelfRespectBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleOtherManDominanceBands", entry.EligibleOtherManDominanceBands ?? string.Empty);
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
            ? "SELECT Id, Name, Description, PhysicalCues, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishFacialTypes ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, PhysicalCues, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishFacialTypes WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishFacialType
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                PhysicalCues = reader.GetString(3), EligibleDesireBands = reader.GetString(4),
                EligibleSelfRespectBands = reader.GetString(5), EligibleOtherManDominanceBands = reader.GetString(6),
                SortOrder = reader.GetInt32(7), IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(9), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(10), out var u) ? u : DateTime.UtcNow
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
                EligibleDesireBands, EligibleSelfRespectBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $physicalCues, $narrativeCue,
                $eligibleDesireBands, $eligibleSelfRespectBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                PhysicalCues = excluded.PhysicalCues,
                NarrativeCue = excluded.NarrativeCue,
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
            ? "SELECT Id, Name, Description, PhysicalCues, NarrativeCue, EligibleDesireBands, EligibleSelfRespectBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishReceptivityLevels ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, PhysicalCues, NarrativeCue, EligibleDesireBands, EligibleSelfRespectBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishReceptivityLevels WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishReceptivityLevel
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                PhysicalCues = reader.GetString(3), NarrativeCue = reader.GetString(4),
                EligibleDesireBands = reader.GetString(5), EligibleSelfRespectBands = reader.GetString(6),
                SortOrder = reader.GetInt32(7), IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(9), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(10), out var u) ? u : DateTime.UtcNow
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
                EligibleOtherManDominanceBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $exampleDialogue,
                $eligibleOtherManDominanceBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                ExampleDialogue = excluded.ExampleDialogue,
                EligibleOtherManDominanceBands = excluded.EligibleOtherManDominanceBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$exampleDialogue", entry.ExampleDialogue ?? string.Empty);
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
            ? "SELECT Id, Name, Description, ExampleDialogue, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishHisControlLevels ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, ExampleDialogue, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishHisControlLevels WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishHisControlLevel
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                ExampleDialogue = reader.GetString(3), EligibleOtherManDominanceBands = reader.GetString(4),
                SortOrder = reader.GetInt32(5), IsEnabled = reader.GetInt32(6) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(7), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var u) ? u : DateTime.UtcNow
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
                EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $name, $description, $transitionText,
                $eligibleDesireBands, $eligibleSelfRespectBands, $eligibleOtherManDominanceBands,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Description = excluded.Description,
                TransitionText = excluded.TransitionText,
                EligibleDesireBands = excluded.EligibleDesireBands,
                EligibleSelfRespectBands = excluded.EligibleSelfRespectBands,
                EligibleOtherManDominanceBands = excluded.EligibleOtherManDominanceBands,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description ?? string.Empty);
        command.Parameters.AddWithValue("$transitionText", entry.TransitionText ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleDesireBands", entry.EligibleDesireBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleSelfRespectBands", entry.EligibleSelfRespectBands ?? string.Empty);
        command.Parameters.AddWithValue("$eligibleOtherManDominanceBands", entry.EligibleOtherManDominanceBands ?? string.Empty);
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
            ? "SELECT Id, Name, Description, TransitionText, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishTransitionActions ORDER BY SortOrder, Id"
            : "SELECT Id, Name, Description, TransitionText, EligibleDesireBands, EligibleSelfRespectBands, EligibleOtherManDominanceBands, SortOrder, IsEnabled, CreatedUtc, UpdatedUtc FROM RPFinishTransitionActions WHERE IsEnabled = 1 ORDER BY SortOrder, Id";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RPFinishTransitionAction
            {
                Id = reader.GetString(0), Name = reader.GetString(1), Description = reader.GetString(2),
                TransitionText = reader.GetString(3), EligibleDesireBands = reader.GetString(4),
                EligibleSelfRespectBands = reader.GetString(5), EligibleOtherManDominanceBands = reader.GetString(6),
                SortOrder = reader.GetInt32(7), IsEnabled = reader.GetInt32(8) == 1,
                CreatedUtc = DateTime.TryParse(reader.GetString(9), out var c) ? c : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(10), out var u) ? u : DateTime.UtcNow
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
            if (string.IsNullOrWhiteSpace(desireBand) || string.IsNullOrWhiteSpace(selfRespectBand) || string.IsNullOrWhiteSpace(otherManDominanceBand))
            {
                continue;
            }

            var row = new RPFinishingMoveMatrixRow
            {
                Id = GetString(item, "id") ?? Guid.NewGuid().ToString("N"),
                DesireBand = desireBand,
                SelfRespectBand = selfRespectBand,
                OtherManDominanceBand = otherManDominanceBand,
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
            "DELETE FROM RPThemePhaseGuidance WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeGuidancePoints WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeAIGuidanceNotes WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeFitRules WHERE ThemeId = $themeId",
            "DELETE FROM RPThemeNarrativeGateRules WHERE ThemeId = $themeId"
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
            cmd.CommandText = "INSERT INTO RPThemePhaseGuidance (Id, ThemeId, Phase, GuidanceText) VALUES ($id, $themeId, $phase, $guidanceText)";
            cmd.Parameters.AddWithValue("$id", string.IsNullOrWhiteSpace(guidance.Id) ? Guid.NewGuid().ToString("N") : guidance.Id);
            cmd.Parameters.AddWithValue("$themeId", theme.Id);
            cmd.Parameters.AddWithValue("$phase", guidance.Phase.ToString());
            cmd.Parameters.AddWithValue("$guidanceText", guidance.GuidanceText);
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
            ["Optional Variations Within This Scenario"] = RPThemeAIGuidanceSection.Variation
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
                Id, DesireBand, SelfRespectBand, OtherManDominanceBand,
                PrimaryLocationsJson, SecondaryLocationsJson, ExcludedLocationsJson,
                WifeReceptivity, WifeBehaviorModifier, OtherManBehaviorModifier, TransitionInstruction,
                SortOrder, IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $desireBand, $selfRespectBand, $otherManDominanceBand,
                $primaryLocationsJson, $secondaryLocationsJson, $excludedLocationsJson,
                $wifeReceptivity, $wifeBehaviorModifier, $otherManBehaviorModifier, $transitionInstruction,
                $sortOrder, $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                DesireBand = excluded.DesireBand,
                SelfRespectBand = excluded.SelfRespectBand,
                OtherManDominanceBand = excluded.OtherManDominanceBand,
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
        command.CommandText = "SELECT Id, ThemeId, Phase, GuidanceText FROM RPThemePhaseGuidance WHERE ThemeId = $themeId";
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
                GuidanceText = reader.GetString(3)
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

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        if (!_supplementalTablesEnsured)
        {
            await EnsureSupplementalTablesAsync(connection, cancellationToken);
            await EnsureRpThemesColumnsAsync(connection, cancellationToken);
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

    private static async Task EnsureSupplementalTablesAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await MigrateLegacyMatrixTablesToGlobalAsync(connection, cancellationToken);
        await MigrateFinishingMoveMatrixToV2Async(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS RPFinishingMoveMatrixRows (
                Id TEXT PRIMARY KEY,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                OtherManDominanceBand TEXT NOT NULL,
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
                UNIQUE (DesireBand, SelfRespectBand, OtherManDominanceBand)
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishingMoveMatrixRows_Sort
                ON RPFinishingMoveMatrixRows (SortOrder, Id);

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

            CREATE TABLE IF NOT EXISTS RPFinishLocations (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                EligibleDesireBands TEXT NOT NULL DEFAULT '',
                EligibleSelfRespectBands TEXT NOT NULL DEFAULT '',
                EligibleOtherManDominanceBands TEXT NOT NULL DEFAULT '',
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
                EligibleDesireBands TEXT NOT NULL DEFAULT '',
                EligibleSelfRespectBands TEXT NOT NULL DEFAULT '',
                EligibleOtherManDominanceBands TEXT NOT NULL DEFAULT '',
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
                EligibleDesireBands TEXT NOT NULL DEFAULT '',
                EligibleSelfRespectBands TEXT NOT NULL DEFAULT '',
                EligibleOtherManDominanceBands TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPFinishTransitionActions_Sort
                ON RPFinishTransitionActions (SortOrder, Id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
