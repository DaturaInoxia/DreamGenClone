using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.Administration;
using DreamGenClone.Domain.ModelManager;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Domain.StoryParser;
using DreamGenClone.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DreamGenClone.Infrastructure.Persistence;

public sealed class SqlitePersistence : ISqlitePersistence
{
    private const string LegacyMigrationVersionKey = "LegacyMigrationsVersion";
    private const string CurrentLegacyMigrationVersion = "2026-05-01-1";

    private readonly PersistenceOptions _options;
    private readonly LmStudioOptions _lmStudioOptions;
    private readonly StoryAnalysisOptions _storyAnalysisOptions;
    private readonly ScenarioAdaptationOptions _scenarioAdaptationOptions;
    private readonly ILogger<SqlitePersistence> _logger;

    public SqlitePersistence(
        IOptions<PersistenceOptions> options,
        IOptions<LmStudioOptions> lmStudioOptions,
        IOptions<StoryAnalysisOptions> storyAnalysisOptions,
        IOptions<ScenarioAdaptationOptions> scenarioAdaptationOptions,
        ILogger<SqlitePersistence> logger)
    {
        _options = options.Value;
        _lmStudioOptions = lmStudioOptions.Value;
        _storyAnalysisOptions = storyAnalysisOptions.Value;
        _scenarioAdaptationOptions = scenarioAdaptationOptions.Value;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (!string.IsNullOrWhiteSpace(connectionStringBuilder.DataSource))
        {
            var dataSourcePath = Path.GetFullPath(connectionStringBuilder.DataSource);
            var dataDirectory = Path.GetDirectoryName(dataSourcePath);
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }
        }

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Sessions (
                Id TEXT PRIMARY KEY,
                SessionType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                AdaptiveStateJson TEXT NULL,
                SchemaVersion TEXT NOT NULL DEFAULT 'v1',
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Templates (
                Id TEXT PRIMARY KEY,
                TemplateType TEXT NOT NULL,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                ImagePath TEXT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Scenarios (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ParsedStories (
                Id TEXT PRIMARY KEY,
                SourceUrl TEXT NOT NULL,
                SourceDomain TEXT NOT NULL,
                Title TEXT NULL,
                Author TEXT NULL,
                ParsedUtc TEXT NOT NULL,
                PageCount INTEGER NOT NULL,
                CombinedText TEXT NOT NULL,
                StructuredPayloadJson TEXT NOT NULL,
                ParseStatus TEXT NOT NULL,
                DiagnosticsSummaryJson TEXT NOT NULL,
                IsArchived INTEGER NOT NULL DEFAULT 0,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ParsedStories_ParsedUtc ON ParsedStories (ParsedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_SourceDomain ON ParsedStories (SourceDomain);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_Title ON ParsedStories (Title);
            CREATE INDEX IF NOT EXISTS IX_ParsedStories_SourceUrl ON ParsedStories (SourceUrl);

            CREATE TABLE IF NOT EXISTS StorySummaries (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                SummaryText TEXT NOT NULL,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_StorySummaries_ParsedStoryId ON StorySummaries (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS StoryAnalyses (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                CharactersJson TEXT NULL,
                ThemesJson TEXT NULL,
                PlotStructureJson TEXT NULL,
                WritingStyleJson TEXT NULL,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_StoryAnalyses_ParsedStoryId ON StoryAnalyses (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS ThemePreferences (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL DEFAULT '',
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Tier TEXT NOT NULL DEFAULT 'Neutral',
                CatalogId TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ThemeProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ToneProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Intensity TEXT NOT NULL,
                BuildUpPhaseOffset INTEGER NOT NULL DEFAULT 0,
                CommittedPhaseOffset INTEGER NOT NULL DEFAULT 0,
                ApproachingPhaseOffset INTEGER NOT NULL DEFAULT 1,
                ClimaxPhaseOffset INTEGER NOT NULL DEFAULT 2,
                ResetPhaseOffset INTEGER NOT NULL DEFAULT -1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BaseStatProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                TargetGender TEXT NOT NULL DEFAULT 'Unknown',
                TargetRole TEXT NOT NULL DEFAULT 'Unknown',
                DefaultStatsJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS StatWillingnessProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                TargetStatName TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                ThresholdsJson TEXT NOT NULL DEFAULT '[]',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS StatResistanceProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                TargetStatName TEXT NOT NULL DEFAULT 'Loyalty',
                IsDefault INTEGER NOT NULL DEFAULT 0,
                ThresholdsJson TEXT NOT NULL DEFAULT '[]',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_StatResistanceProfiles_Name
                ON StatResistanceProfiles (Name);

            CREATE TABLE IF NOT EXISTS NarrativeGateProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsDefault INTEGER NOT NULL DEFAULT 0,
                RulesJson TEXT NOT NULL DEFAULT '[]',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS HusbandAwarenessProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                AwarenessLevel INTEGER NOT NULL DEFAULT 0,
                AcceptanceLevel INTEGER NOT NULL DEFAULT 0,
                VoyeurismLevel INTEGER NOT NULL DEFAULT 0,
                ParticipationLevel INTEGER NOT NULL DEFAULT 0,
                HumiliationDesire INTEGER NOT NULL DEFAULT 0,
                EncouragementLevel INTEGER NOT NULL DEFAULT 0,
                RiskTolerance INTEGER NOT NULL DEFAULT 0,
                Notes TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS BackgroundCharacterProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CharacterProfiles (
                Id                  TEXT NOT NULL PRIMARY KEY,
                Name                TEXT NOT NULL,
                Description         TEXT NOT NULL DEFAULT '',
                TargetGender        TEXT NOT NULL DEFAULT 'Any',
                TargetRole          TEXT NOT NULL DEFAULT 'Any',
                CharacterStatsJson  TEXT NOT NULL DEFAULT '{}',
                EncounterStatsJson  TEXT NOT NULL DEFAULT '{}',
                AdditionalNotes     TEXT NOT NULL DEFAULT '',
                FullOverride        INTEGER NOT NULL DEFAULT 0,
                IsSeeded            INTEGER NOT NULL DEFAULT 0,
                CreatedUtc          TEXT NOT NULL,
                UpdatedUtc          TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RoleDefinitions (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                UseForAdaptiveProfiles INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS StyleProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL,
                Example TEXT NOT NULL,
                RuleOfThumb TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ThemeCatalog (
                Id TEXT PRIMARY KEY,
                Label TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Keywords TEXT NOT NULL DEFAULT '[]',
                Weight INTEGER NOT NULL DEFAULT 1,
                Category TEXT NOT NULL DEFAULT '',
                StatAffinities TEXT NOT NULL DEFAULT '{}',
                ScenarioFitRules TEXT NOT NULL DEFAULT '',
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ScenarioDefinitions (
                Id TEXT PRIMARY KEY,
                Label TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                Weight INTEGER NOT NULL DEFAULT 1,
                VariantOf TEXT NOT NULL DEFAULT '',
                IsScenarioDefining INTEGER NOT NULL DEFAULT 1,
                Keywords TEXT NOT NULL DEFAULT '[]',
                DirectionalKeywords TEXT NOT NULL DEFAULT '[]',
                StatAffinities TEXT NOT NULL DEFAULT '{}',
                ScenarioFitRules TEXT NOT NULL DEFAULT '',
                PhaseGuidance TEXT NOT NULL DEFAULT '',
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_ScenarioDefinitions_Enabled_Label
                ON ScenarioDefinitions (IsEnabled, Label);

            CREATE TABLE IF NOT EXISTS RPThemeProfiles (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                IsDefault INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RPThemes (
                Id TEXT PRIMARY KEY,
                NarrativeGateProfileId TEXT NULL,
                Label TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                Weight INTEGER NOT NULL DEFAULT 1,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemes_Label
                ON RPThemes (Label);

            CREATE TABLE IF NOT EXISTS RPThemeRelationships (
                ParentThemeId TEXT NOT NULL,
                ChildThemeId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (ParentThemeId, ChildThemeId),
                FOREIGN KEY (ParentThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE,
                FOREIGN KEY (ChildThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

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

            CREATE TABLE IF NOT EXISTS RPThemeKeywords (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                GroupName TEXT NOT NULL DEFAULT '',
                Keyword TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeKeywords_Theme_Sort
                ON RPThemeKeywords (ThemeId, SortOrder, Id);

            CREATE TABLE IF NOT EXISTS RPThemeStatAffinities (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                StatName TEXT NOT NULL,
                Value INTEGER NOT NULL,
                Rationale TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeStatAffinities_Theme_Stat
                ON RPThemeStatAffinities (ThemeId, StatName);

            CREATE TABLE IF NOT EXISTS RPThemeFitRules (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                RoleName TEXT NOT NULL,
                RoleWeight REAL NOT NULL DEFAULT 1,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS RPThemeFitRuleClauses (
                Id TEXT PRIMARY KEY,
                FitRuleId TEXT NOT NULL,
                StatName TEXT NOT NULL,
                Comparator TEXT NOT NULL,
                Threshold REAL NOT NULL,
                PenaltyWeight REAL NOT NULL DEFAULT 1,
                Description TEXT NOT NULL DEFAULT '',
                FOREIGN KEY (FitRuleId) REFERENCES RPThemeFitRules(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS RPThemePhaseGuidance (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                Phase TEXT NOT NULL,
                GuidanceText TEXT NOT NULL,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS RPThemeGuidancePoints (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                Phase TEXT NOT NULL,
                PointType TEXT NOT NULL,
                Text TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

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

            CREATE TABLE IF NOT EXISTS RPThemeSemanticEventMappings (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                EventId TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Delta REAL NOT NULL,
                ConfidenceMin REAL NOT NULL,
                ConfidenceMax REAL NOT NULL,
                ReasonCode TEXT NOT NULL,
                AttributionKey TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeSemanticEventMappings_Theme_Sort
                ON RPThemeSemanticEventMappings (ThemeId, SortOrder, Id);

            CREATE INDEX IF NOT EXISTS IX_RPThemeSemanticEventMappings_Event
                ON RPThemeSemanticEventMappings (EventId, ThemeId);

            CREATE TABLE IF NOT EXISTS RPThemeSemanticStatMappings (
                Id TEXT PRIMARY KEY,
                ThemeId TEXT NOT NULL,
                EventId TEXT NOT NULL,
                TargetStat TEXT NOT NULL,
                Direction TEXT NOT NULL,
                Delta REAL NOT NULL,
                ConfidenceMin REAL NOT NULL,
                ConfidenceMax REAL NOT NULL,
                ReasonCode TEXT NOT NULL,
                AttributionKey TEXT NOT NULL DEFAULT '',
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_RPThemeSemanticStatMappings_Theme_Sort
                ON RPThemeSemanticStatMappings (ThemeId, SortOrder, Id);

            CREATE INDEX IF NOT EXISTS IX_RPThemeSemanticStatMappings_Event
                ON RPThemeSemanticStatMappings (EventId, ThemeId, TargetStat);

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

            CREATE TABLE IF NOT EXISTS RPThemeProfileThemeAssignments (
                Id TEXT PRIMARY KEY,
                ProfileId TEXT NOT NULL,
                ThemeId TEXT NOT NULL,
                Tier TEXT NOT NULL,
                Weight REAL NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                FOREIGN KEY (ProfileId) REFERENCES RPThemeProfiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (ThemeId) REFERENCES RPThemes(Id) ON DELETE CASCADE,
                UNIQUE (ProfileId, ThemeId)
            );

            CREATE TABLE IF NOT EXISTS RPFinishingMoveMatrixRows (
                Id TEXT PRIMARY KEY,
                DesireBand TEXT NOT NULL,
                SelfRespectBand TEXT NOT NULL,
                DominanceBand TEXT NOT NULL,
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
                UNIQUE (DesireBand, SelfRespectBand, DominanceBand)
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

            CREATE TABLE IF NOT EXISTS RPThemeImportRuns (
                Id TEXT PRIMARY KEY,
                StartedUtc TEXT NOT NULL,
                CompletedUtc TEXT NOT NULL,
                ImportedCount INTEGER NOT NULL DEFAULT 0,
                WarningCount INTEGER NOT NULL DEFAULT 0,
                ErrorCount INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS RPThemeImportIssues (
                Id TEXT PRIMARY KEY,
                ImportRunId TEXT NOT NULL,
                SourcePath TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Message TEXT NOT NULL,
                FOREIGN KEY (ImportRunId) REFERENCES RPThemeImportRuns(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SteeringGenerationRecords (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                GenerationPrompt TEXT NOT NULL,
                GenerationResponse TEXT NOT NULL,
                ParsedOptionsJson TEXT NULL,
                CharacterSnapshotJson TEXT NULL,
                ActiveThemeId TEXT NULL,
                ActiveThemeLabel TEXT NULL,
                Phase TEXT NULL,
                ModelIdentifier TEXT NULL,
                ProviderName TEXT NULL,
                Temperature REAL NULL,
                TopP REAL NULL,
                MaxTokens INTEGER NULL,
                Succeeded INTEGER NOT NULL DEFAULT 1,
                ErrorMessage TEXT NULL,
                SelectedDirectiveSummary TEXT NULL,
                StagedInteractionId TEXT NULL,
                ContinuationInteractionId TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_SteeringGenerationRecords_Session
                ON SteeringGenerationRecords (SessionId, CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS StoryRankings (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL,
                ProfileId TEXT NOT NULL DEFAULT '',
                ThemeSnapshotJson TEXT NOT NULL,
                ThemeDetectionsJson TEXT NOT NULL,
                Score REAL NOT NULL,
                IsDisqualified INTEGER NOT NULL DEFAULT 0,
                GeneratedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                UNIQUE(ParsedStoryId, ProfileId)
            );

            CREATE TABLE IF NOT EXISTS StoryCollections (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Description TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_StoryCollections_Name ON StoryCollections (Name);

            CREATE TABLE IF NOT EXISTS StoryCollectionMembers (
                Id TEXT PRIMARY KEY,
                CollectionId TEXT NOT NULL,
                ParsedStoryId TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                AddedUtc TEXT NOT NULL,
                FOREIGN KEY (CollectionId) REFERENCES StoryCollections(Id) ON DELETE CASCADE,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                UNIQUE(CollectionId, ParsedStoryId)
            );

            CREATE INDEX IF NOT EXISTS IX_StoryCollectionMembers_CollectionId ON StoryCollectionMembers (CollectionId);
            CREATE INDEX IF NOT EXISTS IX_StoryCollectionMembers_ParsedStoryId ON StoryCollectionMembers (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS UserStoryRatings (
                Id TEXT PRIMARY KEY,
                ParsedStoryId TEXT NOT NULL UNIQUE,
                Stars INTEGER NULL CHECK(Stars IS NULL OR (Stars >= 1 AND Stars <= 5)),
                Comment TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_UserStoryRatings_ParsedStoryId ON UserStoryRatings (ParsedStoryId);

            CREATE TABLE IF NOT EXISTS DatabaseBackups (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                FileName TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                TriggeredBy TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_DatabaseBackups_CreatedUtc ON DatabaseBackups (CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS Providers (
                Id TEXT PRIMARY KEY NOT NULL,
                Name TEXT NOT NULL UNIQUE,
                ProviderType INTEGER NOT NULL,
                BaseUrl TEXT NOT NULL,
                ChatCompletionsPath TEXT NOT NULL DEFAULT '/v1/chat/completions',
                TimeoutSeconds INTEGER NOT NULL DEFAULT 120,
                ApiKeyEncrypted TEXT,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                Notes TEXT
            );

            CREATE TABLE IF NOT EXISTS RegisteredModels (
                Id TEXT PRIMARY KEY NOT NULL,
                ProviderId TEXT NOT NULL,
                ModelIdentifier TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                SupportsThinkingControl INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                ContextWindowSize INTEGER NOT NULL DEFAULT 0,
                Quantization TEXT NOT NULL DEFAULT '',
                ParameterCount TEXT NOT NULL DEFAULT '',
                Notes TEXT,
                FOREIGN KEY (ProviderId) REFERENCES Providers(Id) ON DELETE CASCADE,
                UNIQUE (ProviderId, ModelIdentifier)
            );

            CREATE TABLE IF NOT EXISTS FunctionModelDefaults (
                Id TEXT PRIMARY KEY NOT NULL,
                FunctionName TEXT NOT NULL UNIQUE,
                ModelId TEXT NOT NULL,
                Temperature REAL NOT NULL DEFAULT 0.7,
                TopP REAL NOT NULL DEFAULT 0.9,
                MaxTokens INTEGER NOT NULL DEFAULT 500,
                ThinkingMode INTEGER NOT NULL DEFAULT 0,
                MaxConcurrentJobs INTEGER NULL,
                UpdatedUtc TEXT NOT NULL,
                FOREIGN KEY (ModelId) REFERENCES RegisteredModels(Id)
            );

            CREATE TABLE IF NOT EXISTS HealthCheckResults (
                Id TEXT PRIMARY KEY NOT NULL,
                EntityType INTEGER NOT NULL,
                EntityId TEXT NOT NULL,
                EntityName TEXT NOT NULL,
                ProviderName TEXT NOT NULL,
                IsHealthy INTEGER NOT NULL DEFAULT 0,
                Message TEXT NOT NULL DEFAULT '',
                CheckedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS PromptTestRuns (
                Id TEXT PRIMARY KEY NOT NULL,
                Comment TEXT NULL,
                ModelIdentifier TEXT NOT NULL,
                ModelDisplayName TEXT NOT NULL,
                ProviderName TEXT NOT NULL,
                SystemMessage TEXT NULL,
                UserPrompt TEXT NOT NULL,
                Temperature REAL NOT NULL DEFAULT 0.7,
                TopP REAL NOT NULL DEFAULT 0.9,
                MaxTokens INTEGER NOT NULL DEFAULT 500,
                ResultText TEXT NULL,
                ResultError TEXT NULL,
                PromptCharCount INTEGER NOT NULL DEFAULT 0,
                ResultWordCount INTEGER NOT NULL DEFAULT 0,
                ResultCharCount INTEGER NOT NULL DEFAULT 0,
                ElapsedSeconds REAL NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayDebugEvents (
                Id TEXT PRIMARY KEY NOT NULL,
                SessionId TEXT NOT NULL,
                CorrelationId TEXT NULL,
                InteractionId TEXT NULL,
                EventKind TEXT NOT NULL,
                Severity TEXT NOT NULL,
                ActorName TEXT NULL,
                ModelIdentifier TEXT NULL,
                ProviderName TEXT NULL,
                DurationMs INTEGER NULL,
                Summary TEXT NOT NULL DEFAULT '',
                MetadataJson TEXT NOT NULL DEFAULT '{}',
                CreatedUtc TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_RolePlayDebugEvents_Session_CreatedUtc
                ON RolePlayDebugEvents (SessionId, CreatedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_RolePlayDebugEvents_Kind_CreatedUtc
                ON RolePlayDebugEvents (EventKind, CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2AdaptiveStates (
                SessionId TEXT PRIMARY KEY,
                ActiveScenarioId TEXT NULL,
                ActiveVariantId TEXT NULL,
                CurrentPhase TEXT NOT NULL,
                TurnCountInPhase INTEGER NOT NULL,
                ConsecutiveLeadCount INTEGER NOT NULL,
                LastEvaluationUtc TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ActiveFormulaVersion TEXT NOT NULL,
                SelectedWillingnessProfileId TEXT NULL,
                SelectedNarrativeGateProfileId TEXT NULL,
                HusbandAwarenessProfileId TEXT NULL,
                CharacterEncounterProfileIdsJson TEXT NULL,
                PhaseOverrideFloor TEXT NULL,
                PhaseOverrideScenarioId TEXT NULL,
                PhaseOverrideCycleIndex INTEGER NULL,
                PhaseOverrideSource TEXT NULL,
                PhaseOverrideAppliedUtc TEXT NULL,
                CurrentSceneLocation TEXT NULL,
                CharacterLocationsJson TEXT NOT NULL DEFAULT '[]',
                CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]',
                CharacterSnapshotsJson TEXT NOT NULL,
                ThemeMachineSnapshotJson TEXT NULL,
                GlobalEncounterCount INTEGER NOT NULL DEFAULT 0,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2CandidateEvaluations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                EvaluationId TEXT NOT NULL,
                ScenarioId TEXT NOT NULL,
                StageAWillingnessTier TEXT NOT NULL,
                StageBEligible INTEGER NOT NULL,
                CharacterAlignmentScore REAL NOT NULL DEFAULT 0,
                NarrativeEvidenceScore REAL NOT NULL DEFAULT 0,
                PreferencePriorityScore REAL NOT NULL DEFAULT 0,
                FitScore REAL NOT NULL,
                UnpenalizedFitScore REAL NOT NULL DEFAULT 0,
                Confidence REAL NOT NULL,
                TieBreakKey TEXT NOT NULL,
                Rationale TEXT NOT NULL,
                DetailsJson TEXT NOT NULL DEFAULT '{}',
                EvaluatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2CandidateEvaluations_Session_EvaluatedUtc
                ON RolePlayV2CandidateEvaluations (SessionId, EvaluatedUtc DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2PhaseTransitions (
                TransitionId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                FromPhase TEXT NOT NULL,
                ToPhase TEXT NOT NULL,
                TriggerType TEXT NOT NULL,
                EvidencePayload TEXT NOT NULL,
                ReasonCode TEXT NOT NULL,
                OccurredUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2PhaseTransitions_Session_OccurredUtc
                ON RolePlayV2PhaseTransitions (SessionId, OccurredUtc DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2CompletionMetadata (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                ScenarioId TEXT NOT NULL,
                PeakPhase TEXT NOT NULL,
                ResetReason TEXT NOT NULL,
                StartedUtc TEXT NOT NULL,
                CompletedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2ConceptInjections (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2DecisionPoints (
                DecisionPointId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                ScenarioId TEXT NOT NULL,
                Phase TEXT NOT NULL,
                TriggerSource TEXT NOT NULL,
                ContextSummary TEXT NOT NULL DEFAULT '',
                AskingActorName TEXT NOT NULL DEFAULT '',
                TargetActorId TEXT NOT NULL DEFAULT '',
                TransparencyMode TEXT NOT NULL,
                OptionIdsJson TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2DecisionPoints_Session_CreatedUtc
                ON RolePlayV2DecisionPoints (SessionId, CreatedUtc DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2DecisionOptions (
                OptionId TEXT PRIMARY KEY,
                DecisionPointId TEXT NOT NULL,
                DisplayText TEXT NOT NULL,
                ResponsePreview TEXT NOT NULL DEFAULT '',
                BehaviorStyleHint TEXT NOT NULL DEFAULT '',
                CharacterDirectionInstruction TEXT NOT NULL DEFAULT '',
                ChatInstruction TEXT NOT NULL DEFAULT '',
                VisibilityMode TEXT NOT NULL,
                Prerequisites TEXT NOT NULL,
                StatDeltaMap TEXT NOT NULL,
                IsCustomResponseFallback INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2Turns (
                TurnId TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                TurnIndex INTEGER NOT NULL,
                TurnKind TEXT NOT NULL,
                TriggerSource TEXT NOT NULL,
                InitiatedByActorName TEXT NULL,
                InputInteractionId TEXT NULL,
                OutputInteractionIdsJson TEXT NOT NULL DEFAULT '[]',
                OutputInteractionCount INTEGER NOT NULL DEFAULT 0,
                StartedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL,
                Status TEXT NOT NULL,
                FailureReason TEXT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE (SessionId, TurnIndex)
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2Turns_Session_TurnIndex
                ON RolePlayV2Turns (SessionId, TurnIndex DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2FormulaVersionRefs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                CycleIndex INTEGER NOT NULL,
                FormulaVersionId TEXT NOT NULL,
                Name TEXT NOT NULL,
                ParameterPayload TEXT NOT NULL,
                EffectiveFromUtc TEXT NOT NULL,
                IsDefault INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2UnsupportedSessionErrors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ErrorCode TEXT NOT NULL,
                SessionId TEXT NOT NULL,
                DetectedSchemaVersion TEXT NULL,
                MissingCanonicalStatsJson TEXT NOT NULL,
                RecoveryGuidance TEXT NOT NULL,
                EmittedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2UnsupportedErrors_Session_EmittedUtc
                ON RolePlayV2UnsupportedSessionErrors (SessionId, EmittedUtc DESC);

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

            CREATE TABLE IF NOT EXISTS ClimaxBeatEntries (
                BeatCode TEXT PRIMARY KEY,
                StageNumber INTEGER NOT NULL,
                StageName TEXT NOT NULL,
                SubBeatName TEXT NOT NULL,
                HintsJson TEXT NOT NULL DEFAULT '[]',
                NextBeatCode TEXT NULL,
                MinTurnsBeforeAdvance INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2ThemeScores (
                SessionId TEXT NOT NULL,
                ThemeId TEXT NOT NULL,
                ThemeName TEXT NOT NULL,
                Intensity TEXT NOT NULL DEFAULT 'None',
                Score REAL NOT NULL DEFAULT 0,
                Blocked INTEGER NOT NULL DEFAULT 0,
                SuppressedHitCount INTEGER NOT NULL DEFAULT 0,
                IsScenarioCandidate INTEGER NOT NULL DEFAULT 0,
                NarrativeFitScore REAL NOT NULL DEFAULT 0,
                LastCandidateEvaluationTimeUtc TEXT NULL,
                CompletionCooldownTurns INTEGER NOT NULL DEFAULT 0,
                BreakdownJson TEXT NOT NULL DEFAULT '{}',
                UpdatedUtc TEXT NOT NULL,
                PRIMARY KEY (SessionId, ThemeId)
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2ThemeScores_Session
                ON RolePlayV2ThemeScores (SessionId);

            CREATE TABLE IF NOT EXISTS RolePlayV2ThemeTrackerMeta (
                SessionId TEXT PRIMARY KEY,
                PrimaryThemeId TEXT NULL,
                SecondaryThemeId TEXT NULL,
                ThemeSelectionRule TEXT NOT NULL DEFAULT 'Top1',
                ObservedTurnCount INTEGER NOT NULL DEFAULT 0,
                SelectionMinimumTurns INTEGER NOT NULL DEFAULT 0,
                RecentEvidenceJson TEXT NOT NULL DEFAULT '[]',
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2ScenarioHistory (
                Id TEXT PRIMARY KEY,
                SessionId TEXT NOT NULL,
                ScenarioId TEXT NOT NULL,
                CompletedAtUtc TEXT NOT NULL,
                TurnCount INTEGER NOT NULL DEFAULT 0,
                PeakThemeScore INTEGER NOT NULL DEFAULT 0,
                PeakDesireLevel INTEGER NOT NULL DEFAULT 0,
                AverageRestraintLevel REAL NOT NULL DEFAULT 0,
                Notes TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2PairwiseStats (
                SessionId TEXT NOT NULL,
                SourceCharacterId TEXT NOT NULL,
                TargetCharacterId TEXT NOT NULL,
                StatsJson TEXT NOT NULL DEFAULT '{}',
                UpdatedUtc TEXT NOT NULL,
                PRIMARY KEY (SessionId, SourceCharacterId, TargetCharacterId)
            );

            CREATE TABLE IF NOT EXISTS RolePlayV2SemanticEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SessionId TEXT NOT NULL,
                InteractionId TEXT NOT NULL,
                EventId TEXT NOT NULL,
                Confidence REAL NOT NULL DEFAULT 0,
                MappingId TEXT NOT NULL DEFAULT '',
                Direction TEXT NOT NULL DEFAULT '',
                ThemeTargetsJson TEXT NOT NULL DEFAULT '[]',
                ProcessedUtc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2SemanticEvents_Session_ProcessedUtc
                ON RolePlayV2SemanticEvents (SessionId, ProcessedUtc DESC);

            CREATE TABLE IF NOT EXISTS RolePlayV2EncounterSummaries (
                Id                          TEXT NOT NULL PRIMARY KEY,
                SessionId                   TEXT NOT NULL,
                CharacterId                 TEXT NOT NULL,
                SummaryType                 TEXT NOT NULL,
                CycleIndex                  INTEGER NOT NULL DEFAULT 0,
                FromPhase                   TEXT NOT NULL,
                ToPhase                     TEXT NOT NULL,
                OccurredUtc                 TEXT NOT NULL,
                TurnCountInPhase     INTEGER NOT NULL DEFAULT 0,
                EncounterNumber             INTEGER NOT NULL DEFAULT 0,
                DetectionEvidence           TEXT NULL,
                StartInteractionIndex       INTEGER NOT NULL DEFAULT 0,
                EndInteractionIndex         INTEGER NOT NULL DEFAULT 0,
                SceneLocation               TEXT NULL,
                ActiveThemeId               TEXT NULL,
                FinishingMoveId             TEXT NULL,
                PositionIdsJson             TEXT NULL,
                CharacterStatsSnapshotJson  TEXT NOT NULL DEFAULT '{}',
                TemplateSummary             TEXT NOT NULL DEFAULT '',
                LlmSummary                  TEXT NULL,
                LlmEnhancedUtc              TEXT NULL,
                EnrichmentPrompt            TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_RolePlayV2EncounterSummaries_Session_OccurredUtc
                ON RolePlayV2EncounterSummaries (SessionId, OccurredUtc DESC);

            """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        var metadataCommand = connection.CreateCommand();
        metadataCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS AppMetadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ScenarioEngineSettings (
                Id TEXT PRIMARY KEY,
                SettingsJson TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """;
        await metadataCommand.ExecuteNonQueryAsync(cancellationToken);

        // RP Prompt Redesign (001-rp-prompt-redesign): PhaseRuleOfThumb config table with
        // 6-row seed (Opening, BuildUp, Committed, Approaching, Climax, Reset).
        var phaseRuleOfThumbCommand = connection.CreateCommand();
        phaseRuleOfThumbCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS PhaseRuleOfThumb (
                Id TEXT NOT NULL PRIMARY KEY,
                Phase TEXT NOT NULL,
                RuleOfThumbText TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                UNIQUE(Phase)
            );

            INSERT OR REPLACE INTO PhaseRuleOfThumb (Id, Phase, RuleOfThumbText, CreatedUtc, UpdatedUtc)
            VALUES
            ('phase-rot-opening',    'Opening',    'Favor atmosphere and sensory grounding. Establish the world, the characters, and the mood before any tension begins. Let the reader settle into the setting.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z'),
            ('phase-rot-buildup',    'BuildUp',    'Favor atmosphere, tension, and sensory detail over speed. Let desire accumulate before anything explicit happens. Build anticipation through what characters notice, feel, and almost-do.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z'),
            ('phase-rot-committed',  'Committed',  'Balance atmosphere with forward momentum. The tension is established — now let it simmer. Characters are aware of the dynamic; let that awareness color their interactions without resolution.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z'),
            ('phase-rot-approaching','Approaching','Tighten the pace. The tension is escalating — let proximity, accidental contact, and charged glances carry the scene. Sensory detail should heighten, not linger. The characters are drawn toward the threshold.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z'),
            ('phase-rot-climax',     'Climax',     'The culmination is here. Maintain the evocative, sensory-rich style but with urgency and compression. Every sentence should advance the encounter. Do not slow for atmosphere — the atmosphere IS the encounter now.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z'),
            ('phase-rot-reset',      'Reset',      'Let the emotional aftermath breathe. Sensory detail over action. The intensity has passed — now write the quiet, the guilt, the replay, the return to ordinary texture. The character is alone with what they did.', '2026-07-17T00:00:00Z', '2026-07-17T00:00:00Z');
            """;
        await phaseRuleOfThumbCommand.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Ensured PhaseRuleOfThumb table exists with 6 phase rows");

        // Always apply V2 decision-point additive schema updates, even when legacy migrations are skipped.
        var ensureDecisionContextSummaryColumn = connection.CreateCommand();
        ensureDecisionContextSummaryColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='ContextSummary'";
        var hasDecisionContextSummaryColumnAlways = Convert.ToInt64(await ensureDecisionContextSummaryColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionContextSummaryColumnAlways)
        {
            var alterDecisionContextSummaryAlways = connection.CreateCommand();
            alterDecisionContextSummaryAlways.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN ContextSummary TEXT NOT NULL DEFAULT ''";
            await alterDecisionContextSummaryAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added ContextSummary column");
        }

        var ensureDecisionAskingActorColumn = connection.CreateCommand();
        ensureDecisionAskingActorColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='AskingActorName'";
        var hasDecisionAskingActorColumnAlways = Convert.ToInt64(await ensureDecisionAskingActorColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionAskingActorColumnAlways)
        {
            var alterDecisionAskingActorAlways = connection.CreateCommand();
            alterDecisionAskingActorAlways.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN AskingActorName TEXT NOT NULL DEFAULT ''";
            await alterDecisionAskingActorAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added AskingActorName column");
        }

        var ensureDecisionTargetActorColumn = connection.CreateCommand();
        ensureDecisionTargetActorColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='TargetActorId'";
        var hasDecisionTargetActorColumnAlways = Convert.ToInt64(await ensureDecisionTargetActorColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionTargetActorColumnAlways)
        {
            var alterDecisionTargetActorAlways = connection.CreateCommand();
            alterDecisionTargetActorAlways.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN TargetActorId TEXT NOT NULL DEFAULT ''";
            await alterDecisionTargetActorAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added TargetActorId column");
        }

        var ensureDecisionResponsePreviewColumn = connection.CreateCommand();
        ensureDecisionResponsePreviewColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='ResponsePreview'";
        var hasDecisionResponsePreviewColumnAlways = Convert.ToInt64(await ensureDecisionResponsePreviewColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionResponsePreviewColumnAlways)
        {
            var alterDecisionResponsePreviewAlways = connection.CreateCommand();
            alterDecisionResponsePreviewAlways.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN ResponsePreview TEXT NOT NULL DEFAULT ''";
            await alterDecisionResponsePreviewAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added ResponsePreview column");
        }

        var ensureDecisionBehaviorStyleHintColumn = connection.CreateCommand();
        ensureDecisionBehaviorStyleHintColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='BehaviorStyleHint'";
        var hasDecisionBehaviorStyleHintColumnAlways = Convert.ToInt64(await ensureDecisionBehaviorStyleHintColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionBehaviorStyleHintColumnAlways)
        {
            var alterDecisionBehaviorStyleHintAlways = connection.CreateCommand();
            alterDecisionBehaviorStyleHintAlways.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN BehaviorStyleHint TEXT NOT NULL DEFAULT ''";
            await alterDecisionBehaviorStyleHintAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added BehaviorStyleHint column");
        }

        var ensureDecisionCharacterInstructionColumn = connection.CreateCommand();
        ensureDecisionCharacterInstructionColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='CharacterDirectionInstruction'";
        var hasDecisionCharacterInstructionColumnAlways = Convert.ToInt64(await ensureDecisionCharacterInstructionColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionCharacterInstructionColumnAlways)
        {
            var alterDecisionCharacterInstructionAlways = connection.CreateCommand();
            alterDecisionCharacterInstructionAlways.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN CharacterDirectionInstruction TEXT NOT NULL DEFAULT ''";
            await alterDecisionCharacterInstructionAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added CharacterDirectionInstruction column");
        }

        var ensureDecisionChatInstructionColumn = connection.CreateCommand();
        ensureDecisionChatInstructionColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='ChatInstruction'";
        var hasDecisionChatInstructionColumnAlways = Convert.ToInt64(await ensureDecisionChatInstructionColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionChatInstructionColumnAlways)
        {
            var alterDecisionChatInstructionAlways = connection.CreateCommand();
            alterDecisionChatInstructionAlways.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN ChatInstruction TEXT NOT NULL DEFAULT ''";
            await alterDecisionChatInstructionAlways.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added ChatInstruction column");
        }

        // Always ensure RolePlayV2CandidateEvaluations has UnpenalizedFitScore column.
        var ensureCandidateUnpenalizedFitScoreColumn = connection.CreateCommand();
        ensureCandidateUnpenalizedFitScoreColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='UnpenalizedFitScore'";
        var hasCandidateUnpenalizedFitScoreColumn = Convert.ToInt64(await ensureCandidateUnpenalizedFitScoreColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCandidateUnpenalizedFitScoreColumn)
        {
            var alterCandidateUnpenalizedFitScore = connection.CreateCommand();
            alterCandidateUnpenalizedFitScore.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN UnpenalizedFitScore REAL NOT NULL DEFAULT 0";
            await alterCandidateUnpenalizedFitScore.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added UnpenalizedFitScore column");
        }

        // Always ensure RolePlayV2CandidateEvaluations has SuccessorCausalityBoost column.
        var ensureCandidateSuccessorBoostColumn = connection.CreateCommand();
        ensureCandidateSuccessorBoostColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='SuccessorCausalityBoost'";
        var hasCandidateSuccessorBoostColumn = Convert.ToInt64(await ensureCandidateSuccessorBoostColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCandidateSuccessorBoostColumn)
        {
            var alterCandidateSuccessorBoost = connection.CreateCommand();
            alterCandidateSuccessorBoost.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN SuccessorCausalityBoost REAL NOT NULL DEFAULT 0";
            await alterCandidateSuccessorBoost.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added SuccessorCausalityBoost column");
        }

        // Always ensure ToneProfiles has phase-offset columns, even if legacy migrations are marked complete.
        var ensureToneBuildUpOffset = connection.CreateCommand();
        ensureToneBuildUpOffset.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='BuildUpPhaseOffset'";
        var hasToneBuildUpOffsetAlways = Convert.ToInt64(await ensureToneBuildUpOffset.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasToneBuildUpOffsetAlways)
        {
            var alterToneBuildUpOffsetAlways = connection.CreateCommand();
            alterToneBuildUpOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN BuildUpPhaseOffset INTEGER NOT NULL DEFAULT 0";
            await alterToneBuildUpOffsetAlways.ExecuteNonQueryAsync(cancellationToken);

            var alterToneCommittedOffsetAlways = connection.CreateCommand();
            alterToneCommittedOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN CommittedPhaseOffset INTEGER NOT NULL DEFAULT 0";
            await alterToneCommittedOffsetAlways.ExecuteNonQueryAsync(cancellationToken);

            var alterToneApproachingOffsetAlways = connection.CreateCommand();
            alterToneApproachingOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ApproachingPhaseOffset INTEGER NOT NULL DEFAULT 1";
            await alterToneApproachingOffsetAlways.ExecuteNonQueryAsync(cancellationToken);

            var alterToneClimaxOffsetAlways = connection.CreateCommand();
            alterToneClimaxOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ClimaxPhaseOffset INTEGER NOT NULL DEFAULT 2";
            await alterToneClimaxOffsetAlways.ExecuteNonQueryAsync(cancellationToken);

            var alterToneResetOffsetAlways = connection.CreateCommand();
            alterToneResetOffsetAlways.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ResetPhaseOffset INTEGER NOT NULL DEFAULT -1";
            await alterToneResetOffsetAlways.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated ToneProfiles table: added phase offset columns");
        }

        // Always ensure ToneProfiles has the 5 writing directive columns (plan-amendment 2026-07-22).
        var ensureToneProseStyleDir = connection.CreateCommand();
        ensureToneProseStyleDir.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ToneProfiles') WHERE name='ProseStyleDirective'";
        var hasToneProseStyleDir = Convert.ToInt64(await ensureToneProseStyleDir.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasToneProseStyleDir)
        {
            var alterToneProseStyle = connection.CreateCommand();
            alterToneProseStyle.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ProseStyleDirective TEXT NOT NULL DEFAULT ''";
            await alterToneProseStyle.ExecuteNonQueryAsync(cancellationToken);

            var alterToneVoice = connection.CreateCommand();
            alterToneVoice.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN VoiceDirective TEXT NOT NULL DEFAULT ''";
            await alterToneVoice.ExecuteNonQueryAsync(cancellationToken);

            var alterToneTone = connection.CreateCommand();
            alterToneTone.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN ToneDirective TEXT NOT NULL DEFAULT ''";
            await alterToneTone.ExecuteNonQueryAsync(cancellationToken);

            var alterToneFocus = connection.CreateCommand();
            alterToneFocus.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN FocusDirective TEXT NOT NULL DEFAULT ''";
            await alterToneFocus.ExecuteNonQueryAsync(cancellationToken);

            var alterToneHeatLevel = connection.CreateCommand();
            alterToneHeatLevel.CommandText = "ALTER TABLE ToneProfiles ADD COLUMN HeatLevelDirective TEXT NOT NULL DEFAULT ''";
            await alterToneHeatLevel.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated ToneProfiles table: added 5 writing directive columns");
        }

        // Always ensure RPFinishingMoveMatrixRows has WifeReceptivity column.
        var ensureFinishingMoveWifeReceptivityColumn = connection.CreateCommand();
        ensureFinishingMoveWifeReceptivityColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPFinishingMoveMatrixRows') WHERE name='WifeReceptivity'";
        var hasFinishingMoveWifeReceptivityColumn = Convert.ToInt64(await ensureFinishingMoveWifeReceptivityColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasFinishingMoveWifeReceptivityColumn)
        {
            var alterFinishingMoveWifeReceptivity = connection.CreateCommand();
            alterFinishingMoveWifeReceptivity.CommandText = "ALTER TABLE RPFinishingMoveMatrixRows ADD COLUMN WifeReceptivity TEXT NOT NULL DEFAULT ''";
            await alterFinishingMoveWifeReceptivity.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RPFinishingMoveMatrixRows table: added WifeReceptivity column");
        }

        // Always ensure RPFinishingMoveMatrixRows has EscalationTier column.
        var ensureFinishingMoveEscalationTierColumn = connection.CreateCommand();
        ensureFinishingMoveEscalationTierColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPFinishingMoveMatrixRows') WHERE name='EscalationTier'";
        var hasFinishingMoveEscalationTierColumn = Convert.ToInt64(await ensureFinishingMoveEscalationTierColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasFinishingMoveEscalationTierColumn)
        {
            var alterFinishingMoveEscalationTier = connection.CreateCommand();
            alterFinishingMoveEscalationTier.CommandText = "ALTER TABLE RPFinishingMoveMatrixRows ADD COLUMN EscalationTier TEXT NOT NULL DEFAULT 'Low'";
            await alterFinishingMoveEscalationTier.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RPFinishingMoveMatrixRows table: added EscalationTier column");
        }

        // Always ensure RPPositions has EscalationTier column.
        var ensurePositionEscalationTierColumn = connection.CreateCommand();
        ensurePositionEscalationTierColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPPositions') WHERE name='EscalationTier'";
        var hasPositionEscalationTierColumn = Convert.ToInt64(await ensurePositionEscalationTierColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPositionEscalationTierColumn)
        {
            var alterPositionEscalationTier = connection.CreateCommand();
            alterPositionEscalationTier.CommandText = "ALTER TABLE RPPositions ADD COLUMN EscalationTier TEXT NOT NULL DEFAULT 'Low'";
            await alterPositionEscalationTier.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RPPositions table: added EscalationTier column");
        }

        var checkAdaptiveStateJsonColumn = connection.CreateCommand();
        checkAdaptiveStateJsonColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='AdaptiveStateJson'";
        var hasAdaptiveStateJsonColumn = Convert.ToInt64(await checkAdaptiveStateJsonColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasAdaptiveStateJsonColumn)
        {
            var alterAdaptiveStateJson = connection.CreateCommand();
            alterAdaptiveStateJson.CommandText = "ALTER TABLE Sessions ADD COLUMN AdaptiveStateJson TEXT NULL";
            await alterAdaptiveStateJson.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated Sessions table: added AdaptiveStateJson column");
        }

        var checkMaxMilestonesToInjectColumn = connection.CreateCommand();
        checkMaxMilestonesToInjectColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='MaxMilestonesToInject'";
        var hasMaxMilestonesToInjectColumn = Convert.ToInt64(await checkMaxMilestonesToInjectColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasMaxMilestonesToInjectColumn)
        {
            var alterMaxMilestonesToInject = connection.CreateCommand();
            alterMaxMilestonesToInject.CommandText = "ALTER TABLE Sessions ADD COLUMN MaxMilestonesToInject INTEGER NULL";
            await alterMaxMilestonesToInject.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated Sessions table: added MaxMilestonesToInject column");
        }

        // B-058 Phase 1.2: per-session overrides for arc/encounter completion injection caps.
        var sessionOverrideColumns = new (string Column, string Ddl)[]
        {
            ("MaxArcCompletionsToInject", "ALTER TABLE Sessions ADD COLUMN MaxArcCompletionsToInject INTEGER NULL"),
            ("MaxEncounterCompletionsToInject", "ALTER TABLE Sessions ADD COLUMN MaxEncounterCompletionsToInject INTEGER NULL"),
        };
        foreach (var (column, ddl) in sessionOverrideColumns)
        {
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='{column}'";
            var present = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!present)
            {
                var alter = connection.CreateCommand();
                alter.CommandText = ddl;
                await alter.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Migrated Sessions table: added {Column} column", column);
            }
        }

        // RP Prompt Redesign (001-rp-prompt-redesign): 6 new per-session prompt config columns.
        var promptConfigColumns = new (string Column, string Ddl)[]
        {
            ("MaxPromptChars", "ALTER TABLE Sessions ADD COLUMN MaxPromptChars INTEGER NULL"),
            ("ContextWindowTurns", "ALTER TABLE Sessions ADD COLUMN ContextWindowTurns INTEGER NULL"),
            ("ScenarioCompressionTurnThreshold", "ALTER TABLE Sessions ADD COLUMN ScenarioCompressionTurnThreshold INTEGER NULL"),
            ("HistoryFullDetailTurnBand", "ALTER TABLE Sessions ADD COLUMN HistoryFullDetailTurnBand INTEGER NULL"),
            ("HistoryNarrativeOnlyTurnBand", "ALTER TABLE Sessions ADD COLUMN HistoryNarrativeOnlyTurnBand INTEGER NULL"),
            ("SessionMemoryLongTermTurnThreshold", "ALTER TABLE Sessions ADD COLUMN SessionMemoryLongTermTurnThreshold INTEGER NULL"),
        };
        foreach (var (column, ddl) in promptConfigColumns)
        {
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='{column}'";
            var present = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!present)
            {
                var alter = connection.CreateCommand();
                alter.CommandText = ddl;
                await alter.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Migrated Sessions table: added {Column} column", column);
            }
        }

        // B-058 Phase 1.1/2.1: additive columns on RolePlayV2EncounterSummaries for
        // EncounterCompletion tracking (encounter number, detection evidence, interaction range).
        var encounterSummaryColumns = new (string Column, string Ddl)[]
        {
            ("EncounterNumber", "ALTER TABLE RolePlayV2EncounterSummaries ADD COLUMN EncounterNumber INTEGER NOT NULL DEFAULT 0"),
            ("DetectionEvidence", "ALTER TABLE RolePlayV2EncounterSummaries ADD COLUMN DetectionEvidence TEXT NULL"),
            ("StartInteractionIndex", "ALTER TABLE RolePlayV2EncounterSummaries ADD COLUMN StartInteractionIndex INTEGER NOT NULL DEFAULT 0"),
            ("EndInteractionIndex", "ALTER TABLE RolePlayV2EncounterSummaries ADD COLUMN EndInteractionIndex INTEGER NOT NULL DEFAULT 0"),
            ("EnrichmentPrompt", "ALTER TABLE RolePlayV2EncounterSummaries ADD COLUMN EnrichmentPrompt TEXT NULL"),
        };
        foreach (var (column, ddl) in encounterSummaryColumns)
        {
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('RolePlayV2EncounterSummaries') WHERE name='{column}'";
            var present = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!present)
            {
                var alter = connection.CreateCommand();
                alter.CommandText = ddl;
                await alter.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Migrated RolePlayV2EncounterSummaries table: added {Column} column", column);
            }
        }

        // V2 unification (B-038): additive columns on RolePlayV2AdaptiveStates for fields previously
        // held only in V1 Sessions.AdaptiveStateJson. All non-nullable with explicit defaults; existing
        // rows pick up safe initial values.
        var v2AdaptiveExtraColumns = new (string Column, string Ddl)[]
        {
            ("CompletedScenarios", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CompletedScenarios INTEGER NOT NULL DEFAULT 0"),
            ("TurnsSinceCommitment", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TurnsSinceCommitment INTEGER NOT NULL DEFAULT 0"),
            ("TurnsInApproaching", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN TurnsInApproaching INTEGER NOT NULL DEFAULT 0"),
            ("ScenarioCommitmentTimeUtc", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ScenarioCommitmentTimeUtc TEXT NULL"),
            ("SemanticStepSucceeded", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticStepSucceeded INTEGER NOT NULL DEFAULT 1"),
            ("SemanticDeltaBreakdownsJson", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticDeltaBreakdownsJson TEXT NOT NULL DEFAULT '[]'"),
            ("SemanticStatDeltaBreakdownsJson", "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SemanticStatDeltaBreakdownsJson TEXT NOT NULL DEFAULT '[]'"),
        };
        foreach (var (column, ddl) in v2AdaptiveExtraColumns)
        {
            var checkCmd = connection.CreateCommand();
            checkCmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='{column}'";
            var present = Convert.ToInt64(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!present)
            {
                var alter = connection.CreateCommand();
                alter.CommandText = ddl;
                await alter.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added {Column} column", column);
            }
        }

        // B-044: rename Interaction* columns to Turn* on V2 tables and convert stored values
        // from interaction units to turn units (÷3 ceiling). Idempotent — skips already-renamed columns.
        await MigrateV2ColumnsToTurnsAsync(connection, cancellationToken);

        var shouldRunLegacyMigrations = await ShouldRunLegacyMigrationsAsync(connection, cancellationToken);
        if (!shouldRunLegacyMigrations)
        {
            goto AfterLegacyMigrations;
        }

        // Migrate: add Author column if missing (for databases created before this column existed)
        var migrateCmd = connection.CreateCommand();
        migrateCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ParsedStories') WHERE name='Author'";
        var hasAuthor = Convert.ToInt64(await migrateCmd.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasAuthor)
        {
            var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE ParsedStories ADD COLUMN Author TEXT NULL";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ParsedStories table: added Author column");
        }

        var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ParsedStories_Author ON ParsedStories (Author)";
        await indexCmd.ExecuteNonQueryAsync(cancellationToken);

        var checkSchemaVersionColumn = connection.CreateCommand();
        checkSchemaVersionColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='SchemaVersion'";
        var hasSchemaVersionColumn = Convert.ToInt64(await checkSchemaVersionColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasSchemaVersionColumn)
        {
            var alterSchemaVersion = connection.CreateCommand();
            alterSchemaVersion.CommandText = "ALTER TABLE Sessions ADD COLUMN SchemaVersion TEXT NOT NULL DEFAULT 'v1'";
            await alterSchemaVersion.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated Sessions table: added SchemaVersion column");
        }

        var checkScenarioFitRulesColumn = connection.CreateCommand();
        checkScenarioFitRulesColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ThemeCatalog') WHERE name='ScenarioFitRules'";
        var hasScenarioFitRulesColumn = Convert.ToInt64(await checkScenarioFitRulesColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasScenarioFitRulesColumn)
        {
            var alterScenarioFitRules = connection.CreateCommand();
            alterScenarioFitRules.CommandText = "ALTER TABLE ThemeCatalog ADD COLUMN ScenarioFitRules TEXT NOT NULL DEFAULT ''";
            await alterScenarioFitRules.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ThemeCatalog table: added ScenarioFitRules column");
        }

        var checkAlignmentScoreColumn = connection.CreateCommand();
        checkAlignmentScoreColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='CharacterAlignmentScore'";
        var hasAlignmentScoreColumn = Convert.ToInt64(await checkAlignmentScoreColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasAlignmentScoreColumn)
        {
            var alterAlignment = connection.CreateCommand();
            alterAlignment.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN CharacterAlignmentScore REAL NOT NULL DEFAULT 0";
            await alterAlignment.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added CharacterAlignmentScore column");
        }

        var checkNarrativeScoreColumn = connection.CreateCommand();
        checkNarrativeScoreColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='NarrativeEvidenceScore'";
        var hasNarrativeScoreColumn = Convert.ToInt64(await checkNarrativeScoreColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasNarrativeScoreColumn)
        {
            var alterNarrative = connection.CreateCommand();
            alterNarrative.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN NarrativeEvidenceScore REAL NOT NULL DEFAULT 0";
            await alterNarrative.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added NarrativeEvidenceScore column");
        }

        var checkPreferenceScoreColumn = connection.CreateCommand();
        checkPreferenceScoreColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='PreferencePriorityScore'";
        var hasPreferenceScoreColumn = Convert.ToInt64(await checkPreferenceScoreColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPreferenceScoreColumn)
        {
            var alterPreference = connection.CreateCommand();
            alterPreference.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN PreferencePriorityScore REAL NOT NULL DEFAULT 0";
            await alterPreference.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added PreferencePriorityScore column");
        }

        var checkDecisionContextSummaryColumn = connection.CreateCommand();
        checkDecisionContextSummaryColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='ContextSummary'";
        var hasDecisionContextSummaryColumn = Convert.ToInt64(await checkDecisionContextSummaryColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionContextSummaryColumn)
        {
            var alterDecisionContextSummary = connection.CreateCommand();
            alterDecisionContextSummary.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN ContextSummary TEXT NOT NULL DEFAULT ''";
            await alterDecisionContextSummary.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added ContextSummary column");
        }

        var checkDecisionAskingActorColumn = connection.CreateCommand();
        checkDecisionAskingActorColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='AskingActorName'";
        var hasDecisionAskingActorColumn = Convert.ToInt64(await checkDecisionAskingActorColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionAskingActorColumn)
        {
            var alterDecisionAskingActor = connection.CreateCommand();
            alterDecisionAskingActor.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN AskingActorName TEXT NOT NULL DEFAULT ''";
            await alterDecisionAskingActor.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added AskingActorName column");
        }

        var checkDecisionTargetActorColumn = connection.CreateCommand();
        checkDecisionTargetActorColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionPoints') WHERE name='TargetActorId'";
        var hasDecisionTargetActorColumn = Convert.ToInt64(await checkDecisionTargetActorColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionTargetActorColumn)
        {
            var alterDecisionTargetActor = connection.CreateCommand();
            alterDecisionTargetActor.CommandText = "ALTER TABLE RolePlayV2DecisionPoints ADD COLUMN TargetActorId TEXT NOT NULL DEFAULT ''";
            await alterDecisionTargetActor.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionPoints table: added TargetActorId column");
        }

        var checkDecisionResponsePreviewColumn = connection.CreateCommand();
        checkDecisionResponsePreviewColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='ResponsePreview'";
        var hasDecisionResponsePreviewColumn = Convert.ToInt64(await checkDecisionResponsePreviewColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionResponsePreviewColumn)
        {
            var alterDecisionResponsePreview = connection.CreateCommand();
            alterDecisionResponsePreview.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN ResponsePreview TEXT NOT NULL DEFAULT ''";
            await alterDecisionResponsePreview.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added ResponsePreview column");
        }

        var checkDecisionBehaviorStyleHintColumn = connection.CreateCommand();
        checkDecisionBehaviorStyleHintColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='BehaviorStyleHint'";
        var hasDecisionBehaviorStyleHintColumn = Convert.ToInt64(await checkDecisionBehaviorStyleHintColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionBehaviorStyleHintColumn)
        {
            var alterDecisionBehaviorStyleHint = connection.CreateCommand();
            alterDecisionBehaviorStyleHint.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN BehaviorStyleHint TEXT NOT NULL DEFAULT ''";
            await alterDecisionBehaviorStyleHint.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added BehaviorStyleHint column");
        }

        var checkDecisionCharacterInstructionColumn = connection.CreateCommand();
        checkDecisionCharacterInstructionColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='CharacterDirectionInstruction'";
        var hasDecisionCharacterInstructionColumn = Convert.ToInt64(await checkDecisionCharacterInstructionColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionCharacterInstructionColumn)
        {
            var alterDecisionCharacterInstruction = connection.CreateCommand();
            alterDecisionCharacterInstruction.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN CharacterDirectionInstruction TEXT NOT NULL DEFAULT ''";
            await alterDecisionCharacterInstruction.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added CharacterDirectionInstruction column");
        }

        var checkDecisionChatInstructionColumn = connection.CreateCommand();
        checkDecisionChatInstructionColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2DecisionOptions') WHERE name='ChatInstruction'";
        var hasDecisionChatInstructionColumn = Convert.ToInt64(await checkDecisionChatInstructionColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDecisionChatInstructionColumn)
        {
            var alterDecisionChatInstruction = connection.CreateCommand();
            alterDecisionChatInstruction.CommandText = "ALTER TABLE RolePlayV2DecisionOptions ADD COLUMN ChatInstruction TEXT NOT NULL DEFAULT ''";
            await alterDecisionChatInstruction.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2DecisionOptions table: added ChatInstruction column");
        }

        var checkDetailsColumn = connection.CreateCommand();
        checkDetailsColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2CandidateEvaluations') WHERE name='DetailsJson'";
        var hasDetailsColumn = Convert.ToInt64(await checkDetailsColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasDetailsColumn)
        {
            var alterDetails = connection.CreateCommand();
            alterDetails.CommandText = "ALTER TABLE RolePlayV2CandidateEvaluations ADD COLUMN DetailsJson TEXT NOT NULL DEFAULT '{}'";
            await alterDetails.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2CandidateEvaluations table: added DetailsJson column");
        }

        var checkRPAssignmentWeightColumn = connection.CreateCommand();
        checkRPAssignmentWeightColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPThemeProfileThemeAssignments') WHERE name='Weight'";
        var hasRPAssignmentWeightColumn = Convert.ToInt64(await checkRPAssignmentWeightColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasRPAssignmentWeightColumn)
        {
            var alterRPAssignmentWeight = connection.CreateCommand();
            alterRPAssignmentWeight.CommandText = "ALTER TABLE RPThemeProfileThemeAssignments ADD COLUMN Weight REAL NOT NULL DEFAULT 0";
            await alterRPAssignmentWeight.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RPThemeProfileThemeAssignments table: added Weight column");
        }

        // Migrate: add IsArchived column to ParsedStories if missing
        var checkIsArchived = connection.CreateCommand();
        checkIsArchived.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ParsedStories') WHERE name='IsArchived'";
        var hasIsArchived = Convert.ToInt64(await checkIsArchived.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasIsArchived)
        {
            var alterArchived = connection.CreateCommand();
            alterArchived.CommandText = "ALTER TABLE ParsedStories ADD COLUMN IsArchived INTEGER NOT NULL DEFAULT 0";
            await alterArchived.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ParsedStories table: added IsArchived column");
        }

        // Migrate: add ProfileId column to RankingCriteria if missing, then migrate to ThemePreferences
        var checkOldCriteria = connection.CreateCommand();
        checkOldCriteria.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RankingCriteria'";
        var hasOldCriteria = Convert.ToInt64(await checkOldCriteria.ExecuteScalarAsync(cancellationToken)) > 0;
        if (hasOldCriteria)
        {
            // Drop old table — data is incompatible with new ThemePreferences schema
            var dropOld = connection.CreateCommand();
            dropOld.CommandText = "DROP TABLE IF EXISTS RankingCriteria";
            await dropOld.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated: dropped old RankingCriteria table (replaced by ThemePreferences)");
        }

        // Migrate StoryRankings: if table is missing new 'Score' column, rebuild with new schema
        var checkOldRankings = connection.CreateCommand();
        checkOldRankings.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StoryRankings') WHERE name='Score'";
        var hasNewScoreColumn = Convert.ToInt64(await checkOldRankings.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasNewScoreColumn)
        {
            var rebuildCmd = connection.CreateCommand();
            rebuildCmd.CommandText = """
                DROP TABLE StoryRankings;
                CREATE TABLE StoryRankings (
                    Id TEXT PRIMARY KEY,
                    ParsedStoryId TEXT NOT NULL,
                    ProfileId TEXT NOT NULL DEFAULT '',
                    ThemeSnapshotJson TEXT NOT NULL,
                    ThemeDetectionsJson TEXT NOT NULL,
                    Score REAL NOT NULL,
                    IsDisqualified INTEGER NOT NULL DEFAULT 0,
                    GeneratedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    FOREIGN KEY (ParsedStoryId) REFERENCES ParsedStories(Id) ON DELETE CASCADE,
                    UNIQUE(ParsedStoryId, ProfileId)
                );
                CREATE INDEX IF NOT EXISTS IX_StoryRankings_ParsedStoryId ON StoryRankings (ParsedStoryId);
                CREATE INDEX IF NOT EXISTS IX_StoryRankings_Score ON StoryRankings (Score DESC);
                """;
            await rebuildCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated StoryRankings table to theme-based schema (old ranking data cleared)");
        }

        // Ensure StoryRankings indexes exist (after any migration)
        var rankingIndexCmd = connection.CreateCommand();
        rankingIndexCmd.CommandText = """
            CREATE INDEX IF NOT EXISTS IX_StoryRankings_ParsedStoryId ON StoryRankings (ParsedStoryId);
            CREATE INDEX IF NOT EXISTS IX_StoryRankings_Score ON StoryRankings (Score DESC);
            """;
        await rankingIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var toneIndexCmd = connection.CreateCommand();
        toneIndexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ToneProfiles_Name ON ToneProfiles (Name)";
        await toneIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var baseStatsIndexCmd = connection.CreateCommand();
        baseStatsIndexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_BaseStatProfiles_Name ON BaseStatProfiles (Name)";
        await baseStatsIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var checkBaseStatGenderColumn = connection.CreateCommand();
        checkBaseStatGenderColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BaseStatProfiles') WHERE name='TargetGender'";
        var hasBaseStatGenderColumn = Convert.ToInt64(await checkBaseStatGenderColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasBaseStatGenderColumn)
        {
            var alterBaseStatGender = connection.CreateCommand();
            alterBaseStatGender.CommandText = "ALTER TABLE BaseStatProfiles ADD COLUMN TargetGender TEXT NOT NULL DEFAULT 'Unknown'";
            await alterBaseStatGender.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated BaseStatProfiles table: added TargetGender column");
        }

        var checkBaseStatRoleColumn = connection.CreateCommand();
        checkBaseStatRoleColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BaseStatProfiles') WHERE name='TargetRole'";
        var hasBaseStatRoleColumn = Convert.ToInt64(await checkBaseStatRoleColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasBaseStatRoleColumn)
        {
            var alterBaseStatRole = connection.CreateCommand();
            alterBaseStatRole.CommandText = "ALTER TABLE BaseStatProfiles ADD COLUMN TargetRole TEXT NOT NULL DEFAULT 'Unknown'";
            await alterBaseStatRole.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated BaseStatProfiles table: added TargetRole column");
        }

        var willingnessIndexCmd = connection.CreateCommand();
        willingnessIndexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_StatWillingnessProfiles_Name ON StatWillingnessProfiles (Name)";
        await willingnessIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var husbandAwarenessIndexCmd = connection.CreateCommand();
        husbandAwarenessIndexCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_HusbandAwarenessProfiles_Name ON HusbandAwarenessProfiles (Name)";
        await husbandAwarenessIndexCmd.ExecuteNonQueryAsync(cancellationToken);

        var checkActiveVariantColumn = connection.CreateCommand();
        checkActiveVariantColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='ActiveVariantId'";
        var hasActiveVariantColumn = Convert.ToInt64(await checkActiveVariantColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasActiveVariantColumn)
        {
            var alterActiveVariant = connection.CreateCommand();
            alterActiveVariant.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ActiveVariantId TEXT NULL";
            await alterActiveVariant.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added ActiveVariantId column");
        }

        var checkWillingnessProfileColumn = connection.CreateCommand();
        checkWillingnessProfileColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='SelectedWillingnessProfileId'";
        var hasWillingnessProfileColumn = Convert.ToInt64(await checkWillingnessProfileColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasWillingnessProfileColumn)
        {
            var alterWillingnessProfile = connection.CreateCommand();
            alterWillingnessProfile.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN SelectedWillingnessProfileId TEXT NULL";
            await alterWillingnessProfile.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added SelectedWillingnessProfileId column");
        }

        var checkHusbandAwarenessProfileColumn = connection.CreateCommand();
        checkHusbandAwarenessProfileColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='HusbandAwarenessProfileId'";
        var hasHusbandAwarenessProfileColumn = Convert.ToInt64(await checkHusbandAwarenessProfileColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasHusbandAwarenessProfileColumn)
        {
            var alterHusbandAwarenessProfile = connection.CreateCommand();
            alterHusbandAwarenessProfile.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN HusbandAwarenessProfileId TEXT NULL";
            await alterHusbandAwarenessProfile.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added HusbandAwarenessProfileId column");
        }

        var checkCurrentSceneLocationColumn = connection.CreateCommand();
        checkCurrentSceneLocationColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='CurrentSceneLocation'";
        var hasCurrentSceneLocationColumn = Convert.ToInt64(await checkCurrentSceneLocationColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCurrentSceneLocationColumn)
        {
            var alterCurrentSceneLocation = connection.CreateCommand();
            alterCurrentSceneLocation.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CurrentSceneLocation TEXT NULL";
            await alterCurrentSceneLocation.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added CurrentSceneLocation column");
        }

        var checkPhaseOverrideFloorColumn = connection.CreateCommand();
        checkPhaseOverrideFloorColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='PhaseOverrideFloor'";
        var hasPhaseOverrideFloorColumn = Convert.ToInt64(await checkPhaseOverrideFloorColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseOverrideFloorColumn)
        {
            var alterPhaseOverrideFloor = connection.CreateCommand();
            alterPhaseOverrideFloor.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideFloor TEXT NULL";
            await alterPhaseOverrideFloor.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added PhaseOverrideFloor column");
        }

        var checkPhaseOverrideScenarioIdColumn = connection.CreateCommand();
        checkPhaseOverrideScenarioIdColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='PhaseOverrideScenarioId'";
        var hasPhaseOverrideScenarioIdColumn = Convert.ToInt64(await checkPhaseOverrideScenarioIdColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseOverrideScenarioIdColumn)
        {
            var alterPhaseOverrideScenarioId = connection.CreateCommand();
            alterPhaseOverrideScenarioId.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideScenarioId TEXT NULL";
            await alterPhaseOverrideScenarioId.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added PhaseOverrideScenarioId column");
        }

        var checkPhaseOverrideCycleIndexColumn = connection.CreateCommand();
        checkPhaseOverrideCycleIndexColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='PhaseOverrideCycleIndex'";
        var hasPhaseOverrideCycleIndexColumn = Convert.ToInt64(await checkPhaseOverrideCycleIndexColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseOverrideCycleIndexColumn)
        {
            var alterPhaseOverrideCycleIndex = connection.CreateCommand();
            alterPhaseOverrideCycleIndex.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideCycleIndex INTEGER NULL";
            await alterPhaseOverrideCycleIndex.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added PhaseOverrideCycleIndex column");
        }

        var checkPhaseOverrideSourceColumn = connection.CreateCommand();
        checkPhaseOverrideSourceColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='PhaseOverrideSource'";
        var hasPhaseOverrideSourceColumn = Convert.ToInt64(await checkPhaseOverrideSourceColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseOverrideSourceColumn)
        {
            var alterPhaseOverrideSource = connection.CreateCommand();
            alterPhaseOverrideSource.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideSource TEXT NULL";
            await alterPhaseOverrideSource.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added PhaseOverrideSource column");
        }

        var checkPhaseOverrideAppliedUtcColumn = connection.CreateCommand();
        checkPhaseOverrideAppliedUtcColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='PhaseOverrideAppliedUtc'";
        var hasPhaseOverrideAppliedUtcColumn = Convert.ToInt64(await checkPhaseOverrideAppliedUtcColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseOverrideAppliedUtcColumn)
        {
            var alterPhaseOverrideAppliedUtc = connection.CreateCommand();
            alterPhaseOverrideAppliedUtc.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN PhaseOverrideAppliedUtc TEXT NULL";
            await alterPhaseOverrideAppliedUtc.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added PhaseOverrideAppliedUtc column");
        }

        var checkCharacterLocationsJsonColumn = connection.CreateCommand();
        checkCharacterLocationsJsonColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='CharacterLocationsJson'";
        var hasCharacterLocationsJsonColumn = Convert.ToInt64(await checkCharacterLocationsJsonColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCharacterLocationsJsonColumn)
        {
            var alterCharacterLocationsJson = connection.CreateCommand();
            alterCharacterLocationsJson.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterLocationsJson TEXT NOT NULL DEFAULT '[]'";
            await alterCharacterLocationsJson.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added CharacterLocationsJson column");
        }

        var checkCharacterLocationPerceptionsJsonColumn = connection.CreateCommand();
        checkCharacterLocationPerceptionsJsonColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='CharacterLocationPerceptionsJson'";
        var hasCharacterLocationPerceptionsJsonColumn = Convert.ToInt64(await checkCharacterLocationPerceptionsJsonColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCharacterLocationPerceptionsJsonColumn)
        {
            var alterCharacterLocationPerceptionsJson = connection.CreateCommand();
            alterCharacterLocationPerceptionsJson.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN CharacterLocationPerceptionsJson TEXT NOT NULL DEFAULT '[]'";
            await alterCharacterLocationPerceptionsJson.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added CharacterLocationPerceptionsJson column");
        }

        var checkThemeMachineSnapshotJsonColumn = connection.CreateCommand();
        checkThemeMachineSnapshotJsonColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RolePlayV2AdaptiveStates') WHERE name='ThemeMachineSnapshotJson'";
        var hasThemeMachineSnapshotJsonColumn = Convert.ToInt64(await checkThemeMachineSnapshotJsonColumn.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasThemeMachineSnapshotJsonColumn)
        {
            var alterThemeMachineSnapshotJson = connection.CreateCommand();
            alterThemeMachineSnapshotJson.CommandText = "ALTER TABLE RolePlayV2AdaptiveStates ADD COLUMN ThemeMachineSnapshotJson TEXT NULL";
            await alterThemeMachineSnapshotJson.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RolePlayV2AdaptiveStates table: added ThemeMachineSnapshotJson column");
        }

        // Migrate: handle RankingProfiles -> ThemeProfiles safely.
        // If both tables exist, merge rows and drop the legacy table to avoid rename collisions.
        var checkOldTable = connection.CreateCommand();
        checkOldTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RankingProfiles'";
        var hasOldTable = Convert.ToInt64(await checkOldTable.ExecuteScalarAsync(cancellationToken)) > 0;

        var checkNewTable = connection.CreateCommand();
        checkNewTable.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ThemeProfiles'";
        var hasThemeProfilesTable = Convert.ToInt64(await checkNewTable.ExecuteScalarAsync(cancellationToken)) > 0;

        if (hasOldTable && !hasThemeProfilesTable)
        {
            var renameTable = connection.CreateCommand();
            renameTable.CommandText = "ALTER TABLE RankingProfiles RENAME TO ThemeProfiles";
            await renameTable.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated: renamed RankingProfiles table to ThemeProfiles");
        }
        else if (hasOldTable && hasThemeProfilesTable)
        {
            var oldHasIsDefaultCmd = connection.CreateCommand();
            oldHasIsDefaultCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RankingProfiles') WHERE name='IsDefault'";
            var oldHasIsDefault = Convert.ToInt64(await oldHasIsDefaultCmd.ExecuteScalarAsync(cancellationToken)) > 0;

            var mergeCmd = connection.CreateCommand();
            mergeCmd.CommandText = oldHasIsDefault
                ? """
                    INSERT OR IGNORE INTO ThemeProfiles (Id, Name, IsDefault, CreatedUtc, UpdatedUtc)
                    SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM RankingProfiles;
                  """
                : """
                    INSERT OR IGNORE INTO ThemeProfiles (Id, Name, IsDefault, CreatedUtc, UpdatedUtc)
                    SELECT Id, Name, 0, CreatedUtc, UpdatedUtc FROM RankingProfiles;
                  """;
            await mergeCmd.ExecuteNonQueryAsync(cancellationToken);

            var dropOldTable = connection.CreateCommand();
            dropOldTable.CommandText = "DROP TABLE IF EXISTS RankingProfiles";
            await dropOldTable.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated: merged RankingProfiles data into ThemeProfiles and dropped legacy table");
        }

        // Migrate: add IsDefault column to ThemeProfiles if missing
        var migrateIsDefault = connection.CreateCommand();
        migrateIsDefault.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ThemeProfiles') WHERE name='IsDefault'";
        var hasIsDefault = Convert.ToInt64(await migrateIsDefault.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasIsDefault)
        {
            var alterIsDefault = connection.CreateCommand();
            alterIsDefault.CommandText = "ALTER TABLE ThemeProfiles ADD COLUMN IsDefault INTEGER NOT NULL DEFAULT 0";
            await alterIsDefault.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated ThemeProfiles table: added IsDefault column");
        }

        // Migrate: add ThemeVerificationStatusJson column to StoryRankings if missing
        var checkVerificationStatus = connection.CreateCommand();
        checkVerificationStatus.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StoryRankings') WHERE name='ThemeVerificationStatusJson'";
        var hasVerificationStatus = Convert.ToInt64(await checkVerificationStatus.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasVerificationStatus)
        {
            var alterVerification = connection.CreateCommand();
            alterVerification.CommandText = "ALTER TABLE StoryRankings ADD COLUMN ThemeVerificationStatusJson TEXT NOT NULL DEFAULT '{}'";
            await alterVerification.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated StoryRankings table: added ThemeVerificationStatusJson column");
        }

        // Migrate: add Notes column to Providers if missing
        var checkProviderNotes = connection.CreateCommand();
        checkProviderNotes.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Providers') WHERE name='Notes'";
        var hasProviderNotes = Convert.ToInt64(await checkProviderNotes.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasProviderNotes)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Providers ADD COLUMN Notes TEXT";
            await alter.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated Providers table: added Notes column");
        }

        // Migrate: add model metadata columns to RegisteredModels if missing
        var checkContextWindow = connection.CreateCommand();
        checkContextWindow.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RegisteredModels') WHERE name='ContextWindowSize'";
        var hasContextWindow = Convert.ToInt64(await checkContextWindow.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasContextWindow)
        {
            var alterCtx = connection.CreateCommand();
            alterCtx.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN ContextWindowSize INTEGER NOT NULL DEFAULT 0";
            await alterCtx.ExecuteNonQueryAsync(cancellationToken);

            var alterQuant = connection.CreateCommand();
            alterQuant.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN Quantization TEXT NOT NULL DEFAULT ''";
            await alterQuant.ExecuteNonQueryAsync(cancellationToken);

            var alterParams = connection.CreateCommand();
            alterParams.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN ParameterCount TEXT NOT NULL DEFAULT ''";
            await alterParams.ExecuteNonQueryAsync(cancellationToken);

            var alterNotes = connection.CreateCommand();
            alterNotes.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN Notes TEXT";
            await alterNotes.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated RegisteredModels table: added ContextWindowSize, Quantization, ParameterCount, Notes columns");
        }

        // Migrate: add ThemeAffinities, EscalatingThemeIds, StatBias columns to StyleProfiles if missing
        var checkThemeAffinities = connection.CreateCommand();
        checkThemeAffinities.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StyleProfiles') WHERE name='ThemeAffinities'";
        var hasThemeAffinities = Convert.ToInt64(await checkThemeAffinities.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasThemeAffinities)
        {
            var alterAffinities = connection.CreateCommand();
            alterAffinities.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN ThemeAffinities TEXT NOT NULL DEFAULT '{}'";
            await alterAffinities.ExecuteNonQueryAsync(cancellationToken);

            var alterEscalating = connection.CreateCommand();
            alterEscalating.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN EscalatingThemeIds TEXT NOT NULL DEFAULT '[]'";
            await alterEscalating.ExecuteNonQueryAsync(cancellationToken);

            var alterStatBias = connection.CreateCommand();
            alterStatBias.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN StatBias TEXT NOT NULL DEFAULT '{}'";
            await alterStatBias.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated StyleProfiles table: added ThemeAffinities, EscalatingThemeIds, StatBias columns");
        }

        // Migrate: add ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax,
        // NarrativeWordTargetMin, NarrativeWordTargetMax columns to StyleProfiles if missing
        var checkImmersionDirective = connection.CreateCommand();
        checkImmersionDirective.CommandText = "SELECT COUNT(*) FROM pragma_table_info('StyleProfiles') WHERE name='ImmersionDirective'";
        var hasImmersionDirective = Convert.ToInt64(await checkImmersionDirective.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasImmersionDirective)
        {
            var alterImmersion = connection.CreateCommand();
            alterImmersion.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN ImmersionDirective TEXT NOT NULL DEFAULT ''";
            await alterImmersion.ExecuteNonQueryAsync(cancellationToken);

            var alterAction = connection.CreateCommand();
            alterAction.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN ActionDirective TEXT NOT NULL DEFAULT ''";
            await alterAction.ExecuteNonQueryAsync(cancellationToken);

            var alterWordMin = connection.CreateCommand();
            alterWordMin.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN WordTargetMin INTEGER NOT NULL DEFAULT 0";
            await alterWordMin.ExecuteNonQueryAsync(cancellationToken);

            var alterWordMax = connection.CreateCommand();
            alterWordMax.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN WordTargetMax INTEGER NOT NULL DEFAULT 0";
            await alterWordMax.ExecuteNonQueryAsync(cancellationToken);

            var alterNarrMin = connection.CreateCommand();
            alterNarrMin.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN NarrativeWordTargetMin INTEGER NOT NULL DEFAULT 0";
            await alterNarrMin.ExecuteNonQueryAsync(cancellationToken);

            var alterNarrMax = connection.CreateCommand();
            alterNarrMax.CommandText = "ALTER TABLE StyleProfiles ADD COLUMN NarrativeWordTargetMax INTEGER NOT NULL DEFAULT 0";
            await alterNarrMax.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated StyleProfiles table: added ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax columns");
        }

        // Migrate: add DirectiveText column to RPThemePhaseGuidance if missing
        var checkPhaseDirective = connection.CreateCommand();
        checkPhaseDirective.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RPThemePhaseGuidance') WHERE name='DirectiveText'";
        var hasPhaseDirective = Convert.ToInt64(await checkPhaseDirective.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasPhaseDirective)
        {
            var alterPhaseDirective = connection.CreateCommand();
            alterPhaseDirective.CommandText = "ALTER TABLE RPThemePhaseGuidance ADD COLUMN DirectiveText TEXT NOT NULL DEFAULT ''";
            await alterPhaseDirective.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated RPThemePhaseGuidance table: added DirectiveText column");
        }

        // Migrate: add CatalogId column to ThemePreferences if missing
        var checkCatalogId = connection.CreateCommand();
        checkCatalogId.CommandText = "SELECT COUNT(*) FROM pragma_table_info('ThemePreferences') WHERE name='CatalogId'";
        var hasCatalogId = Convert.ToInt64(await checkCatalogId.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasCatalogId)
        {
            var alterCatalogId = connection.CreateCommand();
            alterCatalogId.CommandText = "ALTER TABLE ThemePreferences ADD COLUMN CatalogId TEXT NOT NULL DEFAULT ''";
            await alterCatalogId.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Migrated ThemePreferences table: added CatalogId column");
        }

        // B-042: Migrate BaseStatProfiles and HusbandAwarenessProfiles → CharacterProfiles
        // Delete the synthetic "Balanced Baseline" seed that is superseded by per-role unified archetypes.
        var deleteBalancedBaseline = connection.CreateCommand();
        deleteBalancedBaseline.CommandText = "DELETE FROM BaseStatProfiles WHERE Name = 'Balanced Baseline'";
        await deleteBalancedBaseline.ExecuteNonQueryAsync(cancellationToken);

        // Migrate BaseStatProfiles → CharacterProfiles (INSERT OR IGNORE = safe to re-run)
        var migrateBaseStats = connection.CreateCommand();
        migrateBaseStats.CommandText = """
            INSERT OR IGNORE INTO CharacterProfiles
                (Id, Name, Description, TargetGender, TargetRole,
                 CharacterStatsJson, EncounterStatsJson, AdditionalNotes,
                 FullOverride, IsSeeded, CreatedUtc, UpdatedUtc)
            SELECT
                Id, Name, Description,
                COALESCE(TargetGender, 'Any'), COALESCE(TargetRole, 'Any'),
                COALESCE(DefaultStatsJson, '{}'), '{}', '',
                0, 1, CreatedUtc, UpdatedUtc
            FROM BaseStatProfiles;
            """;
        await migrateBaseStats.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("B-042: Migrated BaseStatProfiles → CharacterProfiles");

        // Migrate HusbandAwarenessProfiles → CharacterProfiles (INSERT OR IGNORE = safe to re-run)
        var migrateHusband = connection.CreateCommand();
        migrateHusband.CommandText = """
            INSERT OR IGNORE INTO CharacterProfiles
                (Id, Name, Description, TargetGender, TargetRole,
                 CharacterStatsJson, EncounterStatsJson, AdditionalNotes,
                 FullOverride, IsSeeded, CreatedUtc, UpdatedUtc)
            SELECT
                Id, Name, Description,
                'Any', 'Husband',
                '{"Desire":50,"Restraint":50,"Tension":50,"Connection":50,"Dominance":50,"Loyalty":50,"SelfRespect":50}',
                json_object(
                    'Awareness',     AwarenessLevel,
                    'Acceptance',    AcceptanceLevel,
                    'Voyeurism',     VoyeurismLevel,
                    'Participation', ParticipationLevel,
                    'Encouragement', EncouragementLevel,
                    'RiskTolerance', RiskTolerance
                ),
                COALESCE(Notes, ''),
                0, 1, CreatedUtc, UpdatedUtc
            FROM HusbandAwarenessProfiles;
            """;
        await migrateHusband.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("B-042: Migrated HusbandAwarenessProfiles → CharacterProfiles");

        await MarkLegacyMigrationsCompleteAsync(connection, cancellationToken);

    AfterLegacyMigrations:

        // Thinking-control migrations must run for every database, including databases whose
        // legacy migration version is already complete.
        var checkThinkingControl = connection.CreateCommand();
        checkThinkingControl.CommandText = "SELECT COUNT(*) FROM pragma_table_info('RegisteredModels') WHERE name='SupportsThinkingControl'";
        var hasThinkingControl = Convert.ToInt64(await checkThinkingControl.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasThinkingControl)
        {
            var alterThinkingControl = connection.CreateCommand();
            alterThinkingControl.CommandText = "ALTER TABLE RegisteredModels ADD COLUMN SupportsThinkingControl INTEGER NOT NULL DEFAULT 0";
            await alterThinkingControl.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated RegisteredModels table: added SupportsThinkingControl column");
        }

        var checkThinkingMode = connection.CreateCommand();
        checkThinkingMode.CommandText = "SELECT COUNT(*) FROM pragma_table_info('FunctionModelDefaults') WHERE name='ThinkingMode'";
        var hasThinkingMode = Convert.ToInt64(await checkThinkingMode.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasThinkingMode)
        {
            var addThinkingMode = connection.CreateCommand();
            addThinkingMode.CommandText = "ALTER TABLE FunctionModelDefaults ADD COLUMN ThinkingMode INTEGER NOT NULL DEFAULT 0";
            await addThinkingMode.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated FunctionModelDefaults: added ThinkingMode column");
        }

        // 001-opening-period: seed OpeningGuidanceText into scenario payload JSON.
        // Runs unconditionally (outside the legacy-migration gate) so existing DBs get seeded.
        // Idempotent: only sets the field when missing/empty. The value flows to the prompt
        // builder via Scenario.OpeningGuidanceText (deserialized from PayloadJson).
        var openingGuidanceSeed = connection.CreateCommand();
        openingGuidanceSeed.CommandText = """
            UPDATE Scenarios
            SET PayloadJson = json_set(
                PayloadJson,
                '$.OpeningGuidanceText',
                'Introduce the characters and the scenario — who they are, how they fit into their world, and the situation they are in now — grounded in the character profiles and descriptions. State the marriage as it currently is: a settled, long-established couple with a sex life that matches their stats. When their Desire is high and their Restraint is low, they are sexually active and recently intimate — comfortable with each other''s bodies, past courtship and discovery. When their stats are muted, show that instead: a physical life that is routine or subdued. On-screen intimacy is allowed in the opening only when their profiles and current state support it. This is not about them reconnecting or reaching for emotional closeness; their dynamic is already fixed. Sketch their routines, the rhythm of their days, and the setting. Let the potential arcs foreshadow quietly in the background. Keep the focus on the husband and wife; other characters remain in the background.'
            )
            WHERE json_extract(PayloadJson, '$.OpeningGuidanceText') IS NULL
               OR json_extract(PayloadJson, '$.OpeningGuidanceText') = '';
            """;
        var openingGuidanceSeeded = await openingGuidanceSeed.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("001-opening-period: Seeded OpeningGuidanceText on {ScenarioCount} scenario(s)", openingGuidanceSeeded);

        // Seed Model Manager tables on first run (empty Providers table)
        var checkProviders = connection.CreateCommand();
        checkProviders.CommandText = "SELECT COUNT(*) FROM Providers";
        var providerCount = Convert.ToInt64(await checkProviders.ExecuteScalarAsync(cancellationToken));
        if (providerCount == 0)
        {
            var providerId = Guid.NewGuid().ToString();
            var now = DateTime.UtcNow.ToString("o");

            // Resolve model names from options (fallback to LmStudio model)
            var lmModel = _lmStudioOptions.Model;
            var analysisModel = _storyAnalysisOptions.Model ?? lmModel;
            var scenarioModel = _scenarioAdaptationOptions.Model ?? lmModel;

            // Seed LM Studio provider
            var seedProvider = connection.CreateCommand();
            seedProvider.CommandText = """
                INSERT INTO Providers (Id, Name, ProviderType, BaseUrl, ChatCompletionsPath, TimeoutSeconds, ApiKeyEncrypted, IsEnabled, CreatedUtc, UpdatedUtc)
                VALUES ($id, $name, $type, $baseUrl, $path, $timeout, NULL, 1, $now, $now)
                """;
            seedProvider.Parameters.AddWithValue("$id", providerId);
            seedProvider.Parameters.AddWithValue("$name", "LM Studio (Local)");
            seedProvider.Parameters.AddWithValue("$type", (int)ProviderType.LmStudio);
            seedProvider.Parameters.AddWithValue("$baseUrl", _lmStudioOptions.BaseUrl);
            seedProvider.Parameters.AddWithValue("$path", _lmStudioOptions.ChatCompletionsPath);
            seedProvider.Parameters.AddWithValue("$timeout", _lmStudioOptions.TimeoutSeconds);
            seedProvider.Parameters.AddWithValue("$now", now);
            await seedProvider.ExecuteNonQueryAsync(cancellationToken);

            // Seed models — collect unique model identifiers
            var modelIds = new Dictionary<string, string>(); // modelIdentifier -> GUID

            void EnsureModel(string identifier, string displayName)
            {
                if (!modelIds.ContainsKey(identifier))
                    modelIds[identifier] = Guid.NewGuid().ToString();
            }

            EnsureModel(lmModel, lmModel);
            if (analysisModel != lmModel)
                EnsureModel(analysisModel, analysisModel);
            if (scenarioModel != lmModel && scenarioModel != analysisModel)
                EnsureModel(scenarioModel, scenarioModel);

            foreach (var (identifier, modelId) in modelIds)
            {
                var seedModel = connection.CreateCommand();
                seedModel.CommandText = """
                    INSERT INTO RegisteredModels (Id, ProviderId, ModelIdentifier, DisplayName, IsEnabled, CreatedUtc)
                    VALUES ($id, $providerId, $identifier, $displayName, 1, $now)
                    """;
                seedModel.Parameters.AddWithValue("$id", modelId);
                seedModel.Parameters.AddWithValue("$providerId", providerId);
                seedModel.Parameters.AddWithValue("$identifier", identifier);
                seedModel.Parameters.AddWithValue("$displayName", identifier);
                seedModel.Parameters.AddWithValue("$now", now);
                await seedModel.ExecuteNonQueryAsync(cancellationToken);
            }

            // Seed function defaults
            var functionDefaults = new (string FunctionName, string ModelIdentifier, double Temp, double TopP, int MaxTokens)[]
            {
                ("RolePlayGeneration", lmModel, 0.7, 0.9, 500),
                ("StoryModeGeneration", lmModel, 0.7, 0.9, 500),
                ("StorySummarize", analysisModel, _storyAnalysisOptions.SummarizeTemperature, 0.9, _storyAnalysisOptions.SummarizeMaxTokens),
                ("StoryAnalyze", analysisModel, _storyAnalysisOptions.AnalyzeTemperature, 0.9, _storyAnalysisOptions.AnalyzeMaxTokens),
                ("StoryRank", analysisModel, _storyAnalysisOptions.RankTemperature, 0.9, _storyAnalysisOptions.RankMaxTokens),
                ("ScenarioPreview", scenarioModel, _scenarioAdaptationOptions.PreviewTemperature, _scenarioAdaptationOptions.PreviewTopP, _scenarioAdaptationOptions.PreviewMaxTokens),
                ("ScenarioAdapt", scenarioModel, _scenarioAdaptationOptions.AdaptTemperature, _scenarioAdaptationOptions.AdaptTopP, _scenarioAdaptationOptions.AdaptMaxTokens),
                ("WritingAssistant", lmModel, 0.7, 0.9, 500),
                ("RolePlayAssistant", lmModel, 0.7, 0.9, 2000),
            };

            foreach (var (funcName, modelIdentifier, temp, topP, maxTokens) in functionDefaults)
            {
                var seedDefault = connection.CreateCommand();
                seedDefault.CommandText = """
                    INSERT INTO FunctionModelDefaults (Id, FunctionName, ModelId, Temperature, TopP, MaxTokens, UpdatedUtc)
                    VALUES ($id, $funcName, $modelId, $temp, $topP, $maxTokens, $now)
                    """;
                seedDefault.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
                seedDefault.Parameters.AddWithValue("$funcName", funcName);
                seedDefault.Parameters.AddWithValue("$modelId", modelIds[modelIdentifier]);
                seedDefault.Parameters.AddWithValue("$temp", temp);
                seedDefault.Parameters.AddWithValue("$topP", topP);
                seedDefault.Parameters.AddWithValue("$maxTokens", maxTokens);
                seedDefault.Parameters.AddWithValue("$now", now);
                await seedDefault.ExecuteNonQueryAsync(cancellationToken);
            }

            _logger.LogInformation("Model Manager seed migration completed: 1 provider, {ModelCount} models, {DefaultCount} function defaults",
                modelIds.Count, functionDefaults.Length);
        }
        else
        {
            _logger.LogInformation("Model Manager seed migration skipped: {ProviderCount} providers already exist", providerCount);
        }

        // Migrate: bump RolePlayAssistant max tokens from 500 to 2000 for existing databases
        var updateAssistantTokens = connection.CreateCommand();
        updateAssistantTokens.CommandText = """
            UPDATE FunctionModelDefaults SET MaxTokens = 2000, UpdatedUtc = $now
            WHERE FunctionName = 'RolePlayAssistant' AND MaxTokens <= 500
            """;
        updateAssistantTokens.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var updatedRows = await updateAssistantTokens.ExecuteNonQueryAsync(cancellationToken);
        if (updatedRows > 0)
            _logger.LogInformation("Migrated RolePlayAssistant function default: MaxTokens 500 → 2000");

        // Migrate: add MaxConcurrentJobs column to FunctionModelDefaults (semantic model concurrency control)
        var checkMaxConcurrent = connection.CreateCommand();
        checkMaxConcurrent.CommandText = "SELECT COUNT(*) FROM pragma_table_info('FunctionModelDefaults') WHERE name='MaxConcurrentJobs'";
        var hasMaxConcurrentJobs = Convert.ToInt64(await checkMaxConcurrent.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasMaxConcurrentJobs)
        {
            var addMaxConcurrent = connection.CreateCommand();
            addMaxConcurrent.CommandText = "ALTER TABLE FunctionModelDefaults ADD COLUMN MaxConcurrentJobs INTEGER NULL";
            await addMaxConcurrent.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated FunctionModelDefaults: added MaxConcurrentJobs column");
        }

        // Migrate: ensure ScenarioEngineSettings table exists (for databases created before this table was added)
        var checkEngineSettings = connection.CreateCommand();
        checkEngineSettings.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ScenarioEngineSettings'";
        var hasEngineSettingsTable = Convert.ToInt64(await checkEngineSettings.ExecuteScalarAsync(cancellationToken)) > 0;
        if (!hasEngineSettingsTable)
        {
            var createEngineSettings = connection.CreateCommand();
            createEngineSettings.CommandText = """
                CREATE TABLE IF NOT EXISTS ScenarioEngineSettings (
                    Id TEXT PRIMARY KEY,
                    SettingsJson TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """;
            await createEngineSettings.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated: created ScenarioEngineSettings table");
        }

        _logger.LogInformation("SQLite persistence initialized using {ConnectionString}", _options.ConnectionString);
    }

    private static async Task<bool> ShouldRunLegacyMigrationsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var markerCommand = connection.CreateCommand();
        markerCommand.CommandText = "SELECT Value FROM AppMetadata WHERE Key = $key LIMIT 1";
        markerCommand.Parameters.AddWithValue("$key", LegacyMigrationVersionKey);
        var marker = await markerCommand.ExecuteScalarAsync(cancellationToken);
        return !string.Equals(Convert.ToString(marker), CurrentLegacyMigrationVersion, StringComparison.Ordinal);
    }

    private static async Task MarkLegacyMigrationsCompleteAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO AppMetadata (Key, Value, UpdatedUtc)
            VALUES ($key, $value, $updatedUtc)
            ON CONFLICT(Key) DO UPDATE SET
                Value = excluded.Value,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$key", LegacyMigrationVersionKey);
        command.Parameters.AddWithValue("$value", CurrentLegacyMigrationVersion);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("o"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DatabaseBackup> CreateDatabaseBackupAsync(string? displayName, CancellationToken cancellationToken = default)
    {
        var databasePath = ResolveDatabasePath();
        var dataDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("Could not resolve the SQLite data directory.");
        var backupDirectory = Path.Combine(dataDirectory, "backups");
        Directory.CreateDirectory(backupDirectory);

        var backupId = Guid.NewGuid().ToString("N");
        var createdUtc = DateTime.UtcNow;
        var normalizedName = string.IsNullOrWhiteSpace(displayName)
            ? $"Database Backup {createdUtc:yyyy-MM-dd HH:mm:ss} UTC"
            : displayName.Trim();
        var safeLabel = SanitizeFileToken(normalizedName);
        var fileName = $"dreamgenclone-backup-{createdUtc:yyyyMMdd-HHmmss}-{safeLabel}-{backupId[..8]}.db";
        var backupPath = Path.Combine(backupDirectory, fileName);
        var escapedBackupPath = backupPath.Replace("'", "''");

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = $"VACUUM INTO '{escapedBackupPath}'";
        await command.ExecuteNonQueryAsync(cancellationToken);

        var fileInfo = new FileInfo(backupPath);
        if (!fileInfo.Exists)
        {
            throw new InvalidOperationException("SQLite backup completed without creating the expected backup file.");
        }

        _logger.LogInformation("Database backup created at {BackupPath}", backupPath);

        return new DatabaseBackup
        {
            Id = backupId,
            DisplayName = normalizedName,
            FileName = fileName,
            RelativePath = Path.GetRelativePath(dataDirectory, backupPath).Replace('\\', '/'),
            FileSizeBytes = fileInfo.Length,
            TriggeredBy = "manual",
            CreatedUtc = createdUtc
        };
    }

    public async Task SaveScenarioAsync(string id, string name, string payloadJson, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Scenarios (Id, Name, PayloadJson, UpdatedUtc)
            VALUES ($id, $name, $payloadJson, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                PayloadJson = $payloadJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$payloadJson", payloadJson);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario persisted: {ScenarioId}", id);
    }

    public async Task<(string Id, string Name, string PayloadJson, string UpdatedUtc)?> LoadScenarioAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, PayloadJson, UpdatedUtc FROM Scenarios WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            );
        }

        return null;
    }

    public async Task<List<(string Id, string Name, string PayloadJson, string UpdatedUtc)>> LoadAllScenariosAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, PayloadJson, UpdatedUtc FROM Scenarios ORDER BY UpdatedUtc DESC";

        var results = new List<(string, string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)
            ));
        }

        _logger.LogInformation("Loaded {ScenarioCount} scenarios from database", results.Count);
        return results;
    }

    public async Task<bool> DeleteScenarioAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Scenarios WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario deletion attempted: {ScenarioId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task SaveParsedStoryAsync(ParsedStoryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ParsedStories (
                Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived, UpdatedUtc)
            VALUES (
                $id, $sourceUrl, $sourceDomain, $title, $author, $parsedUtc, $pageCount,
                $combinedText, $structuredPayloadJson, $parseStatus, $diagnosticsSummaryJson, $isArchived, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                SourceUrl = $sourceUrl,
                SourceDomain = $sourceDomain,
                Title = $title,
                Author = $author,
                ParsedUtc = $parsedUtc,
                PageCount = $pageCount,
                CombinedText = $combinedText,
                StructuredPayloadJson = $structuredPayloadJson,
                ParseStatus = $parseStatus,
                DiagnosticsSummaryJson = $diagnosticsSummaryJson,
                IsArchived = $isArchived,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", record.Id);
        command.Parameters.AddWithValue("$sourceUrl", record.SourceUrl);
        command.Parameters.AddWithValue("$sourceDomain", record.SourceDomain);
        command.Parameters.AddWithValue("$title", (object?)record.Title ?? DBNull.Value);
        command.Parameters.AddWithValue("$author", (object?)record.Author ?? DBNull.Value);
        command.Parameters.AddWithValue("$parsedUtc", record.ParsedUtc.ToString("O"));
        command.Parameters.AddWithValue("$pageCount", record.PageCount);
        command.Parameters.AddWithValue("$combinedText", record.CombinedText);
        command.Parameters.AddWithValue("$structuredPayloadJson", record.StructuredPayloadJson);
        command.Parameters.AddWithValue("$parseStatus", record.ParseStatus.ToString());
        command.Parameters.AddWithValue("$diagnosticsSummaryJson", record.DiagnosticsSummaryJson);
        command.Parameters.AddWithValue("$isArchived", record.IsArchived ? 1 : 0);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story persisted: {ParsedStoryId}, Status={ParseStatus}, Pages={PageCount}", record.Id, record.ParseStatus, record.PageCount);
    }

    public async Task<ParsedStoryRecord?> LoadParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadParsedStoryRecord(reader);
    }

    public async Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        return await LoadParsedStoriesAsync(sortMode, includeArchived: false, limit, offset, cancellationToken);
    }

    public async Task<List<ParsedStoryRecord>> LoadParsedStoriesAsync(CatalogSortMode sortMode, bool includeArchived, int? limit = null, int? offset = null, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var orderClause = sortMode == CatalogSortMode.UrlTitleAsc
            ? "ORDER BY COALESCE(Title, SourceUrl) ASC, ParsedUtc DESC"
            : "ORDER BY ParsedUtc DESC";

        var whereClause = includeArchived ? "" : "WHERE IsArchived = 0";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            {whereClause}
            {orderClause}
            LIMIT $limit OFFSET $offset;
            """;

        command.Parameters.AddWithValue("$limit", limit ?? 200);
        command.Parameters.AddWithValue("$offset", offset ?? 0);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        _logger.LogInformation("Loaded parsed stories: {Count}, SortMode={SortMode}", records.Count, sortMode);
        return records;
    }

    public async Task<List<ParsedStoryRecord>> SearchParsedStoriesAsync(string query, CatalogSortMode sortMode, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var orderClause = sortMode == CatalogSortMode.UrlTitleAsc
            ? "ORDER BY COALESCE(Title, SourceUrl) ASC, ParsedUtc DESC"
            : "ORDER BY ParsedUtc DESC";

        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            WHERE IsArchived = 0
              AND (SourceUrl LIKE $query
               OR SourceDomain LIKE $query
               OR COALESCE(Title, '') LIKE $query
               OR COALESCE(Author, '') LIKE $query
               OR ParseStatus LIKE $query)
            {orderClause};
            """;

        var like = $"%{query ?? string.Empty}%";
        command.Parameters.AddWithValue("$query", like);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        _logger.LogInformation("Searched parsed stories: Query='{Query}', Count={Count}, SortMode={SortMode}", query, records.Count, sortMode);
        return records;
    }

    public async Task<bool> DeleteParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable foreign keys so ON DELETE CASCADE fires for child tables
        var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ParsedStories WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story hard-deleted: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> ArchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsedStories SET IsArchived = 1, UpdatedUtc = $updatedUtc WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story archived: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> UnarchiveParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsedStories SET IsArchived = 0, UpdatedUtc = $updatedUtc WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story unarchived: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> PurgeParsedStoryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable FK cascade to clean up child rows
        var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = "PRAGMA foreign_keys = ON";
        await fkCmd.ExecuteNonQueryAsync(cancellationToken);

        // Delete child data (summaries, analyses, rankings) via cascade
        // Then clear the story content but keep Id, SourceUrl, Title, Author
        var deleteChildren = connection.CreateCommand();
        deleteChildren.CommandText = """
            DELETE FROM StorySummaries WHERE ParsedStoryId = $id;
            DELETE FROM StoryAnalyses WHERE ParsedStoryId = $id;
            DELETE FROM StoryRankings WHERE ParsedStoryId = $id;
            """;
        deleteChildren.Parameters.AddWithValue("$id", id);
        await deleteChildren.ExecuteNonQueryAsync(cancellationToken);

        var purgeCmd = connection.CreateCommand();
        purgeCmd.CommandText = """
            UPDATE ParsedStories SET
                CombinedText = '',
                StructuredPayloadJson = '[]',
                DiagnosticsSummaryJson = '{}',
                PageCount = 0,
                ParseStatus = 'Purged',
                IsArchived = 1,
                UpdatedUtc = $updatedUtc
            WHERE Id = $id
            """;
        purgeCmd.Parameters.AddWithValue("$id", id);
        purgeCmd.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        var rowsAffected = await purgeCmd.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Parsed story purged (partial delete): {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateCombinedTextAsync(string id, string combinedText, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ParsedStories SET CombinedText = $combinedText WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$combinedText", combinedText);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Combined text updated: {ParsedStoryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task<List<ParsedStoryRecord>> FindBySourceUrlAsync(string sourceUrl, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, SourceUrl, SourceDomain, Title, Author, ParsedUtc, PageCount,
                   CombinedText, StructuredPayloadJson, ParseStatus, DiagnosticsSummaryJson, IsArchived
            FROM ParsedStories
            WHERE SourceUrl = $sourceUrl
            ORDER BY ParsedUtc DESC;
            """;
        command.Parameters.AddWithValue("$sourceUrl", sourceUrl);

        var records = new List<ParsedStoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadParsedStoryRecord(reader));
        }

        return records;
    }

    // --- Story Summary persistence ---

    public async Task SaveStorySummaryAsync(StorySummary summary, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(summary);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StorySummaries (Id, ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $summaryText, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                SummaryText = $summaryText,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", summary.Id);
        command.Parameters.AddWithValue("$parsedStoryId", summary.ParsedStoryId);
        command.Parameters.AddWithValue("$summaryText", summary.SummaryText);
        command.Parameters.AddWithValue("$generatedUtc", summary.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story summary persisted: {ParsedStoryId}", summary.ParsedStoryId);
    }

    public async Task<StorySummary?> LoadStorySummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, SummaryText, GeneratedUtc, UpdatedUtc FROM StorySummaries WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StorySummary
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            SummaryText = reader.GetString(2),
            GeneratedUtc = DateTime.TryParse(reader.GetString(3), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Story Analysis persistence ---

    public async Task SaveStoryAnalysisAsync(StoryAnalysisResult analysis, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryAnalyses (Id, ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $charactersJson, $themesJson, $plotStructureJson, $writingStyleJson, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                CharactersJson = $charactersJson,
                ThemesJson = $themesJson,
                PlotStructureJson = $plotStructureJson,
                WritingStyleJson = $writingStyleJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", analysis.Id);
        command.Parameters.AddWithValue("$parsedStoryId", analysis.ParsedStoryId);
        command.Parameters.AddWithValue("$charactersJson", (object?)analysis.CharactersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$themesJson", (object?)analysis.ThemesJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$plotStructureJson", (object?)analysis.PlotStructureJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$writingStyleJson", (object?)analysis.WritingStyleJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$generatedUtc", analysis.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story analysis persisted: {ParsedStoryId}", analysis.ParsedStoryId);
    }

    public async Task<StoryAnalysisResult?> LoadStoryAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, CharactersJson, ThemesJson, PlotStructureJson, WritingStyleJson, GeneratedUtc, UpdatedUtc FROM StoryAnalyses WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StoryAnalysisResult
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            CharactersJson = reader.IsDBNull(2) ? null : reader.GetString(2),
            ThemesJson = reader.IsDBNull(3) ? null : reader.GetString(3),
            PlotStructureJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            WritingStyleJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            GeneratedUtc = DateTime.TryParse(reader.GetString(6), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Ranking Criteria persistence ---

    public async Task SaveThemePreferenceAsync(ThemePreference preference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preference);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ThemePreferences (Id, ProfileId, Name, Description, Tier, CatalogId, CreatedUtc, UpdatedUtc)
            VALUES ($id, $profileId, $name, $description, $tier, $catalogId, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                ProfileId = $profileId,
                Name = $name,
                Description = $description,
                Tier = $tier,
                CatalogId = $catalogId,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", preference.Id);
        command.Parameters.AddWithValue("$profileId", preference.ProfileId);
        command.Parameters.AddWithValue("$name", preference.Name);
        command.Parameters.AddWithValue("$description", preference.Description);
        command.Parameters.AddWithValue("$tier", preference.Tier.ToString());
        command.Parameters.AddWithValue("$catalogId", preference.CatalogId);
        command.Parameters.AddWithValue("$createdUtc", preference.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme preference persisted: {Id}, Name={Name}", preference.Id, preference.Name);
    }

    public async Task<ThemePreference?> LoadThemePreferenceAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CatalogId, CreatedUtc, UpdatedUtc FROM ThemePreferences WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadThemePreference(reader);
    }

    public async Task<List<ThemePreference>> LoadAllThemePreferencesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CatalogId, CreatedUtc, UpdatedUtc FROM ThemePreferences ORDER BY UpdatedUtc DESC";

        var results = new List<ThemePreference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThemePreference(reader));
        }

        _logger.LogInformation("Loaded {Count} theme preferences from database", results.Count);
        return results;
    }

    public async Task<List<ThemePreference>> LoadThemePreferencesByProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ProfileId, Name, Description, Tier, CatalogId, CreatedUtc, UpdatedUtc FROM ThemePreferences WHERE ProfileId = $profileId ORDER BY UpdatedUtc DESC";
        command.Parameters.AddWithValue("$profileId", profileId);

        var results = new List<ThemePreference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThemePreference(reader));
        }

        return results;
    }

    public async Task<bool> DeleteThemePreferenceAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ThemePreferences WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme preference deletion attempted: {Id}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static ThemePreference ReadThemePreference(SqliteDataReader reader)
    {
        return new ThemePreference
        {
            Id = reader.GetString(0),
            ProfileId = reader.GetString(1),
            Name = reader.GetString(2),
            Description = reader.GetString(3),
            Tier = Enum.TryParse<ThemeTier>(reader.GetString(4), out var tier) ? tier : ThemeTier.Neutral,
            CatalogId = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            CreatedUtc = DateTime.TryParse(reader.GetString(6), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Ranking Profile persistence ---

    public async Task SaveThemeProfileAsync(ThemeProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ThemeProfiles (Id, Name, IsDefault, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $isDefault, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                IsDefault = $isDefault,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<ThemeProfile?> LoadThemeProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM ThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ThemeProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            IsDefault = reader.GetInt32(2) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    public async Task<List<ThemeProfile>> LoadAllThemeProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM ThemeProfiles ORDER BY Name";

        var results = new List<ThemeProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ThemeProfile
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                IsDefault = reader.GetInt32(2) != 0,
                CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
            });
        }

        return results;
    }

    public async Task<bool> DeleteThemeProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Also delete theme preferences belonging to this profile
        var deleteCriteria = connection.CreateCommand();
        deleteCriteria.CommandText = "DELETE FROM ThemePreferences WHERE ProfileId = $profileId";
        deleteCriteria.Parameters.AddWithValue("$profileId", id);
        await deleteCriteria.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ThemeProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    public async Task SetDefaultThemeProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Clear all defaults, then set the requested one
        var clearCmd = connection.CreateCommand();
        clearCmd.CommandText = "UPDATE ThemeProfiles SET IsDefault = 0 WHERE IsDefault = 1";
        await clearCmd.ExecuteNonQueryAsync(cancellationToken);

        var setCmd = connection.CreateCommand();
        setCmd.CommandText = "UPDATE ThemeProfiles SET IsDefault = 1, UpdatedUtc = $updatedUtc WHERE Id = $id";
        setCmd.Parameters.AddWithValue("$id", id);
        setCmd.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        await setCmd.ExecuteNonQueryAsync(cancellationToken);

        _logger.LogInformation("Set default theme profile: {ProfileId}", id);
    }

    public async Task<ThemeProfile?> LoadDefaultThemeProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, IsDefault, CreatedUtc, UpdatedUtc FROM ThemeProfiles WHERE IsDefault = 1 LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new ThemeProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            IsDefault = reader.GetInt32(2) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Tone Profile persistence ---

    public async Task SaveToneProfileAsync(IntensityProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var hasPhaseOffsets = await HasTonePhaseOffsetColumnsAsync(connection, cancellationToken);

        string commandText;
        if (hasPhaseOffsets)
        {
            commandText = """
                INSERT INTO ToneProfiles (
                    Id, Name, Description, Intensity,
                    BuildUpPhaseOffset, CommittedPhaseOffset, ApproachingPhaseOffset, ClimaxPhaseOffset, ResetPhaseOffset,
                    ProseStyleDirective, VoiceDirective, ToneDirective, FocusDirective, HeatLevelDirective,
                    CreatedUtc, UpdatedUtc)
                VALUES (
                    $id, $name, $description, $intensity,
                    $buildUpPhaseOffset, $committedPhaseOffset, $approachingPhaseOffset, $climaxPhaseOffset, $resetPhaseOffset,
                    $proseStyleDirective, $voiceDirective, $toneDirective, $focusDirective, $heatLevelDirective,
                    $createdUtc, $updatedUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = $name,
                    Description = $description,
                    Intensity = $intensity,
                    BuildUpPhaseOffset = $buildUpPhaseOffset,
                    CommittedPhaseOffset = $committedPhaseOffset,
                    ApproachingPhaseOffset = $approachingPhaseOffset,
                    ClimaxPhaseOffset = $climaxPhaseOffset,
                    ResetPhaseOffset = $resetPhaseOffset,
                    ProseStyleDirective = $proseStyleDirective,
                    VoiceDirective = $voiceDirective,
                    ToneDirective = $toneDirective,
                    FocusDirective = $focusDirective,
                    HeatLevelDirective = $heatLevelDirective,
                    UpdatedUtc = $updatedUtc;
                """;
        }
        else
        {
            commandText = """
                INSERT INTO ToneProfiles (Id, Name, Description, Intensity, CreatedUtc, UpdatedUtc)
                VALUES ($id, $name, $description, $intensity, $createdUtc, $updatedUtc)
                ON CONFLICT(Id) DO UPDATE SET
                    Name = $name,
                    Description = $description,
                    Intensity = $intensity,
                    UpdatedUtc = $updatedUtc;
                """;
        }
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$intensity", profile.Intensity.ToString());
        if (hasPhaseOffsets)
        {
            command.Parameters.AddWithValue("$buildUpPhaseOffset", profile.BuildUpPhaseOffset);
            command.Parameters.AddWithValue("$committedPhaseOffset", profile.CommittedPhaseOffset);
            command.Parameters.AddWithValue("$approachingPhaseOffset", profile.ApproachingPhaseOffset);
            command.Parameters.AddWithValue("$climaxPhaseOffset", profile.ClimaxPhaseOffset);
            command.Parameters.AddWithValue("$resetPhaseOffset", profile.ResetPhaseOffset);
            command.Parameters.AddWithValue("$proseStyleDirective", profile.ProseStyleDirective);
            command.Parameters.AddWithValue("$voiceDirective", profile.VoiceDirective);
            command.Parameters.AddWithValue("$toneDirective", profile.ToneDirective);
            command.Parameters.AddWithValue("$focusDirective", profile.FocusDirective);
            command.Parameters.AddWithValue("$heatLevelDirective", profile.HeatLevelDirective);
        }
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Tone profile persisted: {ToneProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<IntensityProfile?> LoadToneProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var hasPhaseOffsets = await HasTonePhaseOffsetColumnsAsync(connection, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = hasPhaseOffsets
            ? "SELECT Id, Name, Description, Intensity, BuildUpPhaseOffset, CommittedPhaseOffset, ApproachingPhaseOffset, ClimaxPhaseOffset, ResetPhaseOffset, ProseStyleDirective, VoiceDirective, ToneDirective, FocusDirective, HeatLevelDirective, CreatedUtc, UpdatedUtc FROM ToneProfiles WHERE Id = $id"
            : "SELECT Id, Name, Description, Intensity, CreatedUtc, UpdatedUtc FROM ToneProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadToneProfile(reader, hasPhaseOffsets);
    }

    public async Task<List<IntensityProfile>> LoadAllToneProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var hasPhaseOffsets = await HasTonePhaseOffsetColumnsAsync(connection, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = hasPhaseOffsets
            ? "SELECT Id, Name, Description, Intensity, BuildUpPhaseOffset, CommittedPhaseOffset, ApproachingPhaseOffset, ClimaxPhaseOffset, ResetPhaseOffset, ProseStyleDirective, VoiceDirective, ToneDirective, FocusDirective, HeatLevelDirective, CreatedUtc, UpdatedUtc FROM ToneProfiles ORDER BY Name"
            : "SELECT Id, Name, Description, Intensity, CreatedUtc, UpdatedUtc FROM ToneProfiles ORDER BY Name";

        var results = new List<IntensityProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadToneProfile(reader, hasPhaseOffsets));
        }

        return results;
    }

    public async Task<bool> DeleteToneProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ToneProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Tone profile deletion attempted: {ToneProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static IntensityProfile ReadToneProfile(SqliteDataReader reader, bool hasPhaseOffsets)
    {
        // hasDirectivesNew: true when the 5 directive columns were added (after phase offsets at cols 4-8, col 9+)
        var hasDirectivesNew = hasPhaseOffsets && reader.FieldCount > 11; // 12+ cols includes directives
        var createdColBase = hasPhaseOffsets ? (hasDirectivesNew ? 14 : 9) : 4;
        var updatedCol = createdColBase + 1;

        return new IntensityProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            Intensity = Enum.TryParse<IntensityLevel>(reader.GetString(3), out var intensity)
                ? intensity
                : IntensityLevel.SensualMature,
            BuildUpPhaseOffset = hasPhaseOffsets ? reader.GetInt32(4) : 0,
            CommittedPhaseOffset = hasPhaseOffsets ? reader.GetInt32(5) : 0,
            ApproachingPhaseOffset = hasPhaseOffsets ? reader.GetInt32(6) : 1,
            ClimaxPhaseOffset = hasPhaseOffsets ? reader.GetInt32(7) : 2,
            ResetPhaseOffset = hasPhaseOffsets ? reader.GetInt32(8) : -1,
            ProseStyleDirective = hasDirectivesNew ? reader.GetString(9) : string.Empty,
            VoiceDirective = hasDirectivesNew ? reader.GetString(10) : string.Empty,
            ToneDirective = hasDirectivesNew ? reader.GetString(11) : string.Empty,
            FocusDirective = hasDirectivesNew ? reader.GetString(12) : string.Empty,
            HeatLevelDirective = hasDirectivesNew ? reader.GetString(13) : string.Empty,
            CreatedUtc = DateTime.TryParse(reader.GetString(createdColBase), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(updatedCol), out var updated) ? updated : DateTime.UtcNow
        };
    }

    private static async Task<bool> HasTonePhaseOffsetColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var check = connection.CreateCommand();
        check.CommandText = """
            SELECT COUNT(*)
            FROM pragma_table_info('ToneProfiles')
            WHERE name IN ('BuildUpPhaseOffset', 'CommittedPhaseOffset', 'ApproachingPhaseOffset', 'ClimaxPhaseOffset', 'ResetPhaseOffset')
            """;

        var count = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken));
        return count == 5;
    }

    // --- Base Stat Profile persistence ---

    public async Task SaveBaseStatProfileAsync(BaseStatProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureBaseStatProfileColumnsAsync(connection, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BaseStatProfiles (Id, Name, Description, TargetGender, TargetRole, DefaultStatsJson, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $targetGender, $targetRole, $defaultStatsJson, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                TargetGender = $targetGender,
                TargetRole = $targetRole,
                DefaultStatsJson = $defaultStatsJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$targetGender", CharacterGenderCatalog.NormalizeForProfile(profile.TargetGender));
        command.Parameters.AddWithValue("$targetRole", CharacterRoleCatalog.Normalize(profile.TargetRole));
        command.Parameters.AddWithValue("$defaultStatsJson", JsonSerializer.Serialize(profile.DefaultStats));
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Base stat profile persisted: {BaseStatProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<BaseStatProfile?> LoadBaseStatProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var hasTargetGender = await HasBaseStatTargetGenderColumnAsync(connection, cancellationToken);
        var hasTargetRole = await HasBaseStatTargetRoleColumnAsync(connection, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = BuildBaseStatProfileSelectSql(hasTargetGender, hasTargetRole, "WHERE Id = $id");
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadBaseStatProfile(reader, hasTargetGender, hasTargetRole);
    }

    public async Task<List<BaseStatProfile>> LoadAllBaseStatProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var hasTargetGender = await HasBaseStatTargetGenderColumnAsync(connection, cancellationToken);
        var hasTargetRole = await HasBaseStatTargetRoleColumnAsync(connection, cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = BuildBaseStatProfileSelectSql(hasTargetGender, hasTargetRole, "ORDER BY Name");

        var results = new List<BaseStatProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadBaseStatProfile(reader, hasTargetGender, hasTargetRole));
        }

        return results;
    }

    public async Task<bool> DeleteBaseStatProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM BaseStatProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Base stat profile deletion attempted: {BaseStatProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static BaseStatProfile ReadBaseStatProfile(SqliteDataReader reader, bool hasTargetGender, bool hasTargetRole)
    {
        var defaultStatsColumnIndex = hasTargetGender && hasTargetRole ? 5 : hasTargetGender || hasTargetRole ? 4 : 3;
        var createdUtcColumnIndex = defaultStatsColumnIndex + 1;
        var updatedUtcColumnIndex = defaultStatsColumnIndex + 2;
        var defaultStatsJson = reader.GetString(defaultStatsColumnIndex);
        Dictionary<string, int>? parsedStats = null;
        try
        {
            parsedStats = JsonSerializer.Deserialize<Dictionary<string, int>>(defaultStatsJson);
        }
        catch
        {
            parsedStats = null;
        }

        return new BaseStatProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            TargetGender = hasTargetGender
                ? CharacterGenderCatalog.NormalizeForProfile(reader.IsDBNull(3) ? null : reader.GetString(3))
                : CharacterGenderCatalog.Unknown,
            TargetRole = hasTargetRole
                ? CharacterRoleCatalog.Normalize(reader.IsDBNull(hasTargetGender ? 4 : 3) ? null : reader.GetString(hasTargetGender ? 4 : 3))
                : CharacterRoleCatalog.Unknown,
            DefaultStats = parsedStats is null
                ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, int>(parsedStats, StringComparer.OrdinalIgnoreCase),
            CreatedUtc = DateTime.TryParse(reader.GetString(createdUtcColumnIndex), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(updatedUtcColumnIndex), out var updated) ? updated : DateTime.UtcNow
        };
    }

    private static async Task<bool> HasBaseStatTargetGenderColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BaseStatProfiles') WHERE name='TargetGender'";
        return Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> HasBaseStatTargetRoleColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('BaseStatProfiles') WHERE name='TargetRole'";
        return Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string BuildBaseStatProfileSelectSql(bool hasTargetGender, bool hasTargetRole, string trailingClause)
    {
        if (hasTargetGender && hasTargetRole)
        {
            return $"SELECT Id, Name, Description, TargetGender, TargetRole, DefaultStatsJson, CreatedUtc, UpdatedUtc FROM BaseStatProfiles {trailingClause}";
        }

        if (hasTargetGender)
        {
            return $"SELECT Id, Name, Description, TargetGender, DefaultStatsJson, CreatedUtc, UpdatedUtc FROM BaseStatProfiles {trailingClause}";
        }

        if (hasTargetRole)
        {
            return $"SELECT Id, Name, Description, TargetRole, DefaultStatsJson, CreatedUtc, UpdatedUtc FROM BaseStatProfiles {trailingClause}";
        }

        return $"SELECT Id, Name, Description, DefaultStatsJson, CreatedUtc, UpdatedUtc FROM BaseStatProfiles {trailingClause}";
    }

    private async Task EnsureBaseStatProfileColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (!await HasBaseStatTargetGenderColumnAsync(connection, cancellationToken))
        {
            var alterGender = connection.CreateCommand();
            alterGender.CommandText = "ALTER TABLE BaseStatProfiles ADD COLUMN TargetGender TEXT NOT NULL DEFAULT 'Unknown'";
            await alterGender.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated BaseStatProfiles table on-demand: added TargetGender column");
        }

        if (!await HasBaseStatTargetRoleColumnAsync(connection, cancellationToken))
        {
            var alterRole = connection.CreateCommand();
            alterRole.CommandText = "ALTER TABLE BaseStatProfiles ADD COLUMN TargetRole TEXT NOT NULL DEFAULT 'Unknown'";
            await alterRole.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("Migrated BaseStatProfiles table on-demand: added TargetRole column");
        }
    }

    // B-044: rename Interaction* columns to Turn* on V2 tables and convert stored values
    // from interaction units to turn units (÷3 ceiling). Idempotent — skips already-renamed columns.
    // Also rewrites RPThemeMachineTransitions.GateConfigJson: minimumInteractions → minimumTurns (÷3 ceiling).
    private async Task MigrateV2ColumnsToTurnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Column renames with idempotency: skip if old column missing OR new column already present.
        var renames = new (string Table, string OldCol, string NewCol)[]
        {
            ("RolePlayV2AdaptiveStates",    "InteractionCountInPhase",     "TurnCountInPhase"),
            ("RolePlayV2AdaptiveStates",    "InteractionsSinceCommitment", "TurnsSinceCommitment"),
            ("RolePlayV2AdaptiveStates",    "InteractionsInApproaching",   "TurnsInApproaching"),
            ("RolePlayV2ThemeScores",       "CompletionCooldownInteractions", "CompletionCooldownTurns"),
            ("RolePlayV2ScenarioHistory",   "InteractionCount",            "TurnCount"),
            ("RolePlayV2EncounterSummaries","InteractionCountInPhase",     "TurnCountInPhase"),
        };

        foreach (var (table, oldCol, newCol) in renames)
        {
            var hasOld = await ColumnExistsAsync(connection, table, oldCol, cancellationToken);
            var hasNew = await ColumnExistsAsync(connection, table, newCol, cancellationToken);
            if (!hasOld || hasNew) continue;

            var renameCmd = connection.CreateCommand();
            renameCmd.CommandText = $"ALTER TABLE {table} RENAME COLUMN {oldCol} TO {newCol}";
            await renameCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("B-044 migration: renamed {Table}.{OldCol} → {NewCol}", table, oldCol, newCol);

            // Convert the numeric values from interaction units to turn units (÷3 ceiling).
            var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = $"UPDATE {table} SET {newCol} = MAX(0, ({newCol} + 2) / 3)";
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("B-044 migration: converted {Table}.{NewCol} values (÷3 ceiling)", table, newCol);
        }

        // Rewrite theme machine gate JSON: minimumInteractions → minimumTurns with ÷3 value.
        await MigrateGateConfigJsonToTurnsAsync(connection, cancellationToken);
    }

    private static async Task<bool> ColumnExistsAsync(SqliteConnection connection, string table, string column, CancellationToken cancellationToken)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private async Task MigrateGateConfigJsonToTurnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        // Select rows whose GateConfigJson contains the legacy key.
        var selectCmd = connection.CreateCommand();
        selectCmd.CommandText = """
            SELECT TransitionId, GateConfigJson FROM RPThemeMachineTransitions
            WHERE GateConfigJson LIKE '%minimumInteractions%'
            """;

        await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        var updates = new List<(string TransitionId, string NewJson)>();

        while (await reader.ReadAsync(cancellationToken))
        {
            var transitionId = reader.GetString(0);
            var oldJson = reader.GetString(1);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(oldJson);
                var root = doc.RootElement;

                // Read legacy minimumInteractions if present.
                int legacyValue = 0;
                bool hasLegacy = false;
                if (root.TryGetProperty("minimumInteractions", out var legacyProp)
                    && legacyProp.ValueKind == System.Text.Json.JsonValueKind.Number
                    && legacyProp.TryGetInt32(out legacyValue))
                {
                    hasLegacy = true;
                }

                // Skip rows that already have minimumTurns and no minimumInteractions — done.
                bool hasNew = root.TryGetProperty("minimumTurns", out _);
                if (!hasLegacy && hasNew) continue;
                if (hasNew && !hasLegacy) continue;

                // Determine the turn value.
                int turnValue;
                if (hasNew && root.TryGetProperty("minimumTurns", out var mtProp)
                    && mtProp.ValueKind == System.Text.Json.JsonValueKind.Number
                    && mtProp.TryGetInt32(out var existingMt))
                {
                    turnValue = existingMt; // already migrated, keep
                }
                else
                {
                    turnValue = Math.Max(0, (legacyValue + 2) / 3); // ÷3 ceiling
                }

                // Build new JSON preserving all properties except minimumInteractions.
                using var stream = new MemoryStream();
                using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = false });
                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("minimumInteractions")) continue; // remove legacy key
                    if (prop.NameEquals("minimumTurns"))
                    {
                        writer.WriteNumber("minimumTurns", turnValue);
                        continue;
                    }
                    prop.WriteTo(writer);
                }
                // If minimumTurns was not in the original, add it now.
                if (!root.TryGetProperty("minimumTurns", out _))
                {
                    writer.WriteNumber("minimumTurns", turnValue);
                }
                writer.WriteEndObject();
                writer.Flush();

                var newJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                updates.Add((transitionId, newJson));
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "B-044 migration: failed to parse GateConfigJson for transition {TransitionId}, skipping", transitionId);
            }
        }

        foreach (var (transitionId, newJson) in updates)
        {
            var updateCmd = connection.CreateCommand();
            updateCmd.CommandText = "UPDATE RPThemeMachineTransitions SET GateConfigJson = $json WHERE TransitionId = $id";
            updateCmd.Parameters.AddWithValue("$json", newJson);
            updateCmd.Parameters.AddWithValue("$id", transitionId);
            await updateCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (updates.Count > 0)
        {
            _logger.LogInformation("B-044 migration: rewrote {Count} RPThemeMachineTransitions.GateConfigJson blobs (minimumInteractions → minimumTurns, ÷3)", updates.Count);
        }
    }

    // --- Stat willingness profile persistence ---

    public async Task SaveStatWillingnessProfileAsync(StatWillingnessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StatWillingnessProfiles (Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $targetStatName, $isDefault, $thresholdsJson, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                TargetStatName = $targetStatName,
                IsDefault = $isDefault,
                ThresholdsJson = $thresholdsJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$targetStatName", profile.TargetStatName);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$thresholdsJson", JsonSerializer.Serialize(profile.Thresholds));
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (profile.IsDefault)
        {
            var resetDefaults = connection.CreateCommand();
            resetDefaults.CommandText = "UPDATE StatWillingnessProfiles SET IsDefault = 0 WHERE Id <> $id";
            resetDefaults.Parameters.AddWithValue("$id", profile.Id);
            await resetDefaults.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Stat willingness profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<StatWillingnessProfile?> LoadStatWillingnessProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatWillingnessProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStatWillingnessProfile(reader);
    }

    public async Task<StatWillingnessProfile?> LoadDefaultStatWillingnessProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatWillingnessProfiles WHERE IsDefault = 1 LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadStatWillingnessProfile(reader);
        }

        var fallbackCommand = connection.CreateCommand();
        fallbackCommand.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatWillingnessProfiles ORDER BY UpdatedUtc DESC LIMIT 1";
        await using var fallbackReader = await fallbackCommand.ExecuteReaderAsync(cancellationToken);
        if (!await fallbackReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStatWillingnessProfile(fallbackReader);
    }

    public async Task<List<StatWillingnessProfile>> LoadAllStatWillingnessProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatWillingnessProfiles ORDER BY Name";

        var results = new List<StatWillingnessProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStatWillingnessProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteStatWillingnessProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StatWillingnessProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Stat willingness profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static StatWillingnessProfile ReadStatWillingnessProfile(SqliteDataReader reader)
    {
        List<WillingnessThreshold>? thresholds = null;
        try
        {
            thresholds = JsonSerializer.Deserialize<List<WillingnessThreshold>>(reader.GetString(5));
        }
        catch
        {
            thresholds = null;
        }

        return new StatWillingnessProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            TargetStatName = reader.GetString(3),
            IsDefault = reader.GetInt32(4) == 1,
            Thresholds = thresholds ?? [],
            CreatedUtc = DateTime.TryParse(reader.GetString(6), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Husband awareness profile persistence ---

    // --- Narrative gate profile persistence ---
    // --- Stat resistance profile persistence ---

    public async Task SaveStatResistanceProfileAsync(StatResistanceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StatResistanceProfiles (Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc)
            VALUES (@id, @name, @description, @targetStatName, @isDefault, @thresholdsJson, @createdUtc, @updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = @name,
                Description = @description,
                TargetStatName = @targetStatName,
                IsDefault = @isDefault,
                ThresholdsJson = @thresholdsJson,
                UpdatedUtc = @updatedUtc;
            """;

        command.Parameters.AddWithValue("@id", profile.Id);
        command.Parameters.AddWithValue("@name", profile.Name);
        command.Parameters.AddWithValue("@description", profile.Description);
        command.Parameters.AddWithValue("@targetStatName", profile.TargetStatName);
        command.Parameters.AddWithValue("@isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("@thresholdsJson", JsonSerializer.Serialize(profile.Thresholds));
        command.Parameters.AddWithValue("@createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("@updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (profile.IsDefault)
        {
            var resetDefaults = connection.CreateCommand();
            resetDefaults.CommandText = "UPDATE StatResistanceProfiles SET IsDefault = 0 WHERE Id <> @id";
            resetDefaults.Parameters.AddWithValue("@id", profile.Id);
            await resetDefaults.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Stat resistance profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<StatResistanceProfile?> LoadStatResistanceProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatResistanceProfiles WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStatResistanceProfile(reader);
    }

    public async Task<StatResistanceProfile?> LoadDefaultStatResistanceProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatResistanceProfiles WHERE IsDefault = 1 LIMIT 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadStatResistanceProfile(reader);
        }

        var fallback = connection.CreateCommand();
        fallback.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatResistanceProfiles ORDER BY UpdatedUtc DESC LIMIT 1";
        await using var fallbackReader = await fallback.ExecuteReaderAsync(cancellationToken);
        if (await fallbackReader.ReadAsync(cancellationToken))
        {
            return ReadStatResistanceProfile(fallbackReader);
        }

        return null;
    }

    public async Task<List<StatResistanceProfile>> LoadAllStatResistanceProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, TargetStatName, IsDefault, ThresholdsJson, CreatedUtc, UpdatedUtc FROM StatResistanceProfiles ORDER BY Name";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<StatResistanceProfile>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStatResistanceProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteStatResistanceProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StatResistanceProfiles WHERE Id = @id";
        command.Parameters.AddWithValue("@id", id);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Stat resistance profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rows);
        return rows > 0;
    }

    private static StatResistanceProfile ReadStatResistanceProfile(SqliteDataReader reader)
    {
        List<ResistanceThreshold>? thresholds = null;
        try
        {
            thresholds = JsonSerializer.Deserialize<List<ResistanceThreshold>>(reader.GetString(5));
        }
        catch
        {
            thresholds = null;
        }

        return new StatResistanceProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            TargetStatName = reader.GetString(3),
            IsDefault = reader.GetInt32(4) == 1,
            Thresholds = thresholds ?? [],
            CreatedUtc = DateTime.TryParse(reader.GetString(6), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(7), out var updated) ? updated : DateTime.UtcNow
        };
    }



    public async Task SaveNarrativeGateProfileAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO NarrativeGateProfiles (Id, Name, Description, IsDefault, RulesJson, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $isDefault, $rulesJson, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                IsDefault = $isDefault,
                RulesJson = $rulesJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$isDefault", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$rulesJson", JsonSerializer.Serialize(profile.Rules));
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);

        if (profile.IsDefault)
        {
            var resetDefaults = connection.CreateCommand();
            resetDefaults.CommandText = "UPDATE NarrativeGateProfiles SET IsDefault = 0 WHERE Id <> $id";
            resetDefaults.Parameters.AddWithValue("$id", profile.Id);
            await resetDefaults.ExecuteNonQueryAsync(cancellationToken);
        }

        _logger.LogInformation("Narrative gate profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<NarrativeGateProfile?> LoadNarrativeGateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, RulesJson, CreatedUtc, UpdatedUtc FROM NarrativeGateProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadNarrativeGateProfile(reader);
    }

    public async Task<NarrativeGateProfile?> LoadDefaultNarrativeGateProfileAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, RulesJson, CreatedUtc, UpdatedUtc FROM NarrativeGateProfiles WHERE IsDefault = 1 LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadNarrativeGateProfile(reader);
        }

        var fallbackCommand = connection.CreateCommand();
        fallbackCommand.CommandText = "SELECT Id, Name, Description, IsDefault, RulesJson, CreatedUtc, UpdatedUtc FROM NarrativeGateProfiles ORDER BY UpdatedUtc DESC LIMIT 1";
        await using var fallbackReader = await fallbackCommand.ExecuteReaderAsync(cancellationToken);
        if (!await fallbackReader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadNarrativeGateProfile(fallbackReader);
    }

    public async Task<List<NarrativeGateProfile>> LoadAllNarrativeGateProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, IsDefault, RulesJson, CreatedUtc, UpdatedUtc FROM NarrativeGateProfiles ORDER BY Name";

        var results = new List<NarrativeGateProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadNarrativeGateProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteNarrativeGateProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM NarrativeGateProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Narrative gate profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static NarrativeGateProfile ReadNarrativeGateProfile(SqliteDataReader reader)
    {
        List<NarrativeGateRule>? rules = null;
        try
        {
            rules = JsonSerializer.Deserialize<List<NarrativeGateRule>>(reader.GetString(4));
        }
        catch
        {
            rules = null;
        }

        return new NarrativeGateProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            IsDefault = reader.GetInt32(3) == 1,
            Rules = rules ?? [],
            CreatedUtc = DateTime.TryParse(reader.GetString(5), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(6), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Husband awareness profile persistence ---

    public async Task SaveHusbandAwarenessProfileAsync(HusbandAwarenessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO HusbandAwarenessProfiles (Id, Name, Description, AwarenessLevel, AcceptanceLevel, VoyeurismLevel, ParticipationLevel, HumiliationDesire, EncouragementLevel, RiskTolerance, Notes, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $awarenessLevel, $acceptanceLevel, $voyeurismLevel, $participationLevel, $humiliationDesire, $encouragementLevel, $riskTolerance, $notes, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                AwarenessLevel = $awarenessLevel,
                AcceptanceLevel = $acceptanceLevel,
                VoyeurismLevel = $voyeurismLevel,
                ParticipationLevel = $participationLevel,
                HumiliationDesire = $humiliationDesire,
                EncouragementLevel = $encouragementLevel,
                RiskTolerance = $riskTolerance,
                Notes = $notes,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$awarenessLevel", Math.Clamp(profile.AwarenessLevel, 0, 100));
        command.Parameters.AddWithValue("$acceptanceLevel", Math.Clamp(profile.AcceptanceLevel, 0, 100));
        command.Parameters.AddWithValue("$voyeurismLevel", Math.Clamp(profile.VoyeurismLevel, 0, 100));
        command.Parameters.AddWithValue("$participationLevel", Math.Clamp(profile.ParticipationLevel, 0, 100));
        command.Parameters.AddWithValue("$humiliationDesire", Math.Clamp(profile.HumiliationDesire, 0, 100));
        command.Parameters.AddWithValue("$encouragementLevel", Math.Clamp(profile.EncouragementLevel, 0, 100));
        command.Parameters.AddWithValue("$riskTolerance", Math.Clamp(profile.RiskTolerance, 0, 100));
        command.Parameters.AddWithValue("$notes", profile.Notes);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Husband awareness profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<HusbandAwarenessProfile?> LoadHusbandAwarenessProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, AwarenessLevel, AcceptanceLevel, VoyeurismLevel, ParticipationLevel, HumiliationDesire, EncouragementLevel, RiskTolerance, Notes, CreatedUtc, UpdatedUtc FROM HusbandAwarenessProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadHusbandAwarenessProfile(reader);
    }

    public async Task<List<HusbandAwarenessProfile>> LoadAllHusbandAwarenessProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, AwarenessLevel, AcceptanceLevel, VoyeurismLevel, ParticipationLevel, HumiliationDesire, EncouragementLevel, RiskTolerance, Notes, CreatedUtc, UpdatedUtc FROM HusbandAwarenessProfiles ORDER BY Name";

        var results = new List<HusbandAwarenessProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadHusbandAwarenessProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteHusbandAwarenessProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM HusbandAwarenessProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Husband awareness profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static HusbandAwarenessProfile ReadHusbandAwarenessProfile(SqliteDataReader reader)
    {
        return new HusbandAwarenessProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            AwarenessLevel = reader.GetInt32(3),
            AcceptanceLevel = reader.GetInt32(4),
            VoyeurismLevel = reader.GetInt32(5),
            ParticipationLevel = reader.GetInt32(6),
            HumiliationDesire = reader.GetInt32(7),
            EncouragementLevel = reader.GetInt32(8),
            RiskTolerance = reader.GetInt32(9),
            Notes = reader.GetString(10),
            CreatedUtc = DateTime.TryParse(reader.GetString(11), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(12), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Background character profile persistence ---

    public async Task SaveBackgroundCharacterProfileAsync(BackgroundCharacterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BackgroundCharacterProfiles (Id, Name, Description, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Background character profile persisted: {ProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<BackgroundCharacterProfile?> LoadBackgroundCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM BackgroundCharacterProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadBackgroundCharacterProfile(reader);
    }

    public async Task<List<BackgroundCharacterProfile>> LoadAllBackgroundCharacterProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM BackgroundCharacterProfiles ORDER BY Name";

        var results = new List<BackgroundCharacterProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadBackgroundCharacterProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteBackgroundCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM BackgroundCharacterProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Background character profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static BackgroundCharacterProfile ReadBackgroundCharacterProfile(SqliteDataReader reader)
    {
        return new BackgroundCharacterProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Character profile persistence (B-042) ---

    public async Task SaveCharacterProfileAsync(CharacterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CharacterProfiles
                (Id, Name, Description, TargetGender, TargetRole,
                 CharacterStatsJson, EncounterStatsJson, AdditionalNotes, FullOverride, IsSeeded,
                 CreatedUtc, UpdatedUtc)
            VALUES
                ($id, $name, $description, $targetGender, $targetRole,
                 $characterStatsJson, $encounterStatsJson, $additionalNotes, $fullOverride, $isSeeded,
                 $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name              = $name,
                Description       = $description,
                TargetGender      = $targetGender,
                TargetRole        = $targetRole,
                CharacterStatsJson= $characterStatsJson,
                EncounterStatsJson= $encounterStatsJson,
                AdditionalNotes   = $additionalNotes,
                FullOverride      = $fullOverride,
                UpdatedUtc        = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description ?? "");
        command.Parameters.AddWithValue("$targetGender", profile.TargetGender ?? "Any");
        command.Parameters.AddWithValue("$targetRole", profile.TargetRole ?? "Any");
        command.Parameters.AddWithValue("$characterStatsJson",
            profile.CharacterStats.Count > 0 ? JsonSerializer.Serialize(profile.CharacterStats) : "{}");
        command.Parameters.AddWithValue("$encounterStatsJson",
            profile.EncounterStats.Count > 0 ? JsonSerializer.Serialize(profile.EncounterStats) : "{}");
        command.Parameters.AddWithValue("$additionalNotes", profile.AdditionalNotes ?? "");
        command.Parameters.AddWithValue("$fullOverride", profile.FullOverride ? 1 : 0);
        command.Parameters.AddWithValue("$isSeeded", profile.IsSeeded ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Character profile persisted: {ProfileId}, Name={Name}, Role={Role}", profile.Id, profile.Name, profile.TargetRole);
    }

    public async Task<CharacterProfile?> LoadCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, Description, TargetGender, TargetRole, CharacterStatsJson, EncounterStatsJson, AdditionalNotes, FullOverride, IsSeeded, CreatedUtc, UpdatedUtc " +
            "FROM CharacterProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadCharacterProfile(reader);
    }

    public async Task<List<CharacterProfile>> LoadAllCharacterProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, Description, TargetGender, TargetRole, CharacterStatsJson, EncounterStatsJson, AdditionalNotes, FullOverride, IsSeeded, CreatedUtc, UpdatedUtc " +
            "FROM CharacterProfiles ORDER BY TargetRole, Name";

        var results = new List<CharacterProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCharacterProfile(reader));
        }

        return results;
    }

    public async Task<List<CharacterProfile>> LoadCharacterProfilesByRoleAsync(string targetRole, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, Name, Description, TargetGender, TargetRole, CharacterStatsJson, EncounterStatsJson, AdditionalNotes, FullOverride, IsSeeded, CreatedUtc, UpdatedUtc " +
            "FROM CharacterProfiles WHERE TargetRole = $role ORDER BY Name";
        command.Parameters.AddWithValue("$role", targetRole);

        var results = new List<CharacterProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadCharacterProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CharacterProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Character profile deletion attempted: {ProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static CharacterProfile ReadCharacterProfile(SqliteDataReader reader)
    {
        return new CharacterProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            TargetGender = reader.GetString(3),
            TargetRole = reader.GetString(4),
            CharacterStats = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(5)) ?? [],
            EncounterStats = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(6)) ?? [],
            AdditionalNotes = reader.GetString(7),
            FullOverride = reader.GetInt32(8) != 0,
            IsSeeded = reader.GetInt32(9) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(10), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(11), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Role definition persistence ---

    public async Task SaveRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roleDefinition);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO RoleDefinitions (Id, Name, Description, UseForAdaptiveProfiles, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $useForAdaptiveProfiles, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                UseForAdaptiveProfiles = $useForAdaptiveProfiles,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", roleDefinition.Id);
        command.Parameters.AddWithValue("$name", roleDefinition.Name);
        command.Parameters.AddWithValue("$description", roleDefinition.Description);
        command.Parameters.AddWithValue("$useForAdaptiveProfiles", roleDefinition.UseForAdaptiveProfiles ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", roleDefinition.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Role definition persisted: {RoleId}, Name={Name}", roleDefinition.Id, roleDefinition.Name);
    }

    public async Task<RoleDefinition?> LoadRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, UseForAdaptiveProfiles, CreatedUtc, UpdatedUtc FROM RoleDefinitions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadRoleDefinition(reader);
    }

    public async Task<List<RoleDefinition>> LoadAllRoleDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, UseForAdaptiveProfiles, CreatedUtc, UpdatedUtc FROM RoleDefinitions ORDER BY Name";

        var results = new List<RoleDefinition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadRoleDefinition(reader));
        }

        return results;
    }

    public async Task<bool> DeleteRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM RoleDefinitions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Role definition deletion attempted: {RoleId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static RoleDefinition ReadRoleDefinition(SqliteDataReader reader)
    {
        return new RoleDefinition
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            UseForAdaptiveProfiles = reader.GetInt32(3) == 1,
            CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
        };
    }

    // --- Style Profile persistence ---

    public async Task SaveStyleProfileAsync(SteeringProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StyleProfiles (Id, Name, Description, Example, RuleOfThumb, ThemeAffinities, EscalatingThemeIds, StatBias, CreatedUtc, UpdatedUtc, ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax)
            VALUES ($id, $name, $description, $example, $ruleOfThumb, $themeAffinities, $escalatingThemeIds, $statBias, $createdUtc, $updatedUtc, $immersionDirective, $actionDirective, $wordTargetMin, $wordTargetMax, $narrativeWordTargetMin, $narrativeWordTargetMax)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                Example = $example,
                RuleOfThumb = $ruleOfThumb,
                ThemeAffinities = $themeAffinities,
                EscalatingThemeIds = $escalatingThemeIds,
                StatBias = $statBias,
                UpdatedUtc = $updatedUtc,
                ImmersionDirective = $immersionDirective,
                ActionDirective = $actionDirective,
                WordTargetMin = $wordTargetMin,
                WordTargetMax = $wordTargetMax,
                NarrativeWordTargetMin = $narrativeWordTargetMin,
                NarrativeWordTargetMax = $narrativeWordTargetMax;
            """;

        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$description", profile.Description);
        command.Parameters.AddWithValue("$example", profile.Example);
        command.Parameters.AddWithValue("$ruleOfThumb", profile.RuleOfThumb);
        command.Parameters.AddWithValue("$themeAffinities", JsonSerializer.Serialize(profile.ThemeAffinities));
        command.Parameters.AddWithValue("$escalatingThemeIds", JsonSerializer.Serialize(profile.EscalatingThemeIds));
        command.Parameters.AddWithValue("$statBias", JsonSerializer.Serialize(profile.StatBias));
        command.Parameters.AddWithValue("$createdUtc", profile.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$immersionDirective", profile.ImmersionDirective ?? string.Empty);
        command.Parameters.AddWithValue("$actionDirective", profile.ActionDirective ?? string.Empty);
        command.Parameters.AddWithValue("$wordTargetMin", profile.WordTargetMin);
        command.Parameters.AddWithValue("$wordTargetMax", profile.WordTargetMax);
        command.Parameters.AddWithValue("$narrativeWordTargetMin", profile.NarrativeWordTargetMin);
        command.Parameters.AddWithValue("$narrativeWordTargetMax", profile.NarrativeWordTargetMax);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Style profile persisted: {StyleProfileId}, Name={Name}", profile.Id, profile.Name);
    }

    public async Task<SteeringProfile?> LoadStyleProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, Example, RuleOfThumb, ThemeAffinities, EscalatingThemeIds, StatBias, CreatedUtc, UpdatedUtc, ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax FROM StyleProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStyleProfile(reader);
    }

    public async Task<List<SteeringProfile>> LoadAllStyleProfilesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, Example, RuleOfThumb, ThemeAffinities, EscalatingThemeIds, StatBias, CreatedUtc, UpdatedUtc, ImmersionDirective, ActionDirective, WordTargetMin, WordTargetMax, NarrativeWordTargetMin, NarrativeWordTargetMax FROM StyleProfiles ORDER BY Name";

        var results = new List<SteeringProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStyleProfile(reader));
        }

        return results;
    }

    public async Task<bool> DeleteStyleProfileAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StyleProfiles WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Style profile deletion attempted: {StyleProfileId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static SteeringProfile ReadStyleProfile(SqliteDataReader reader)
    {
        return new SteeringProfile
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.GetString(2),
            Example = reader.GetString(3),
            RuleOfThumb = reader.GetString(4),
            ThemeAffinities = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(5)) ?? new(StringComparer.OrdinalIgnoreCase),
            EscalatingThemeIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? [],
            StatBias = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(7)) ?? new(StringComparer.OrdinalIgnoreCase),
            CreatedUtc = DateTime.TryParse(reader.GetString(8), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(9), out var updated) ? updated : DateTime.UtcNow,
            ImmersionDirective = reader.FieldCount > 10 ? reader.GetString(10) : string.Empty,
            ActionDirective = reader.FieldCount > 11 ? reader.GetString(11) : string.Empty,
            WordTargetMin = reader.FieldCount > 12 ? reader.GetInt32(12) : 0,
            WordTargetMax = reader.FieldCount > 13 ? reader.GetInt32(13) : 0,
            NarrativeWordTargetMin = reader.FieldCount > 14 ? reader.GetInt32(14) : 0,
            NarrativeWordTargetMax = reader.FieldCount > 15 ? reader.GetInt32(15) : 0,
        };
    }

    // --- Theme Catalog persistence ---

    public async Task SaveThemeCatalogEntryAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ThemeCatalog (Id, Label, Description, Keywords, Weight, Category, StatAffinities, ScenarioFitRules, IsEnabled, IsBuiltIn, CreatedUtc, UpdatedUtc)
            VALUES ($id, $label, $description, $keywords, $weight, $category, $statAffinities, $scenarioFitRules, $isEnabled, $isBuiltIn, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Label = $label,
                Description = $description,
                Keywords = $keywords,
                Weight = $weight,
                Category = $category,
                StatAffinities = $statAffinities,
                ScenarioFitRules = $scenarioFitRules,
                IsEnabled = $isEnabled,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$label", entry.Label);
        command.Parameters.AddWithValue("$description", entry.Description);
        command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(entry.Keywords));
        command.Parameters.AddWithValue("$weight", entry.Weight);
        command.Parameters.AddWithValue("$category", entry.Category);
        command.Parameters.AddWithValue("$statAffinities", JsonSerializer.Serialize(entry.StatAffinities));
        command.Parameters.AddWithValue("$scenarioFitRules", entry.ScenarioFitRules ?? string.Empty);
        command.Parameters.AddWithValue("$isEnabled", entry.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$isBuiltIn", entry.IsBuiltIn ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", entry.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme catalog entry persisted: {EntryId}, Label={Label}", entry.Id, entry.Label);
    }

    public async Task<ThemeCatalogEntry?> LoadThemeCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Label, Description, Keywords, Weight, Category, StatAffinities, ScenarioFitRules, IsEnabled, IsBuiltIn, CreatedUtc, UpdatedUtc FROM ThemeCatalog WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadThemeCatalogEntry(reader);
    }

    public async Task<List<ThemeCatalogEntry>> LoadAllThemeCatalogEntriesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Label, Description, Keywords, Weight, Category, StatAffinities, ScenarioFitRules, IsEnabled, IsBuiltIn, CreatedUtc, UpdatedUtc FROM ThemeCatalog ORDER BY Label"
            : "SELECT Id, Label, Description, Keywords, Weight, Category, StatAffinities, ScenarioFitRules, IsEnabled, IsBuiltIn, CreatedUtc, UpdatedUtc FROM ThemeCatalog WHERE IsEnabled = 1 ORDER BY Label";

        var results = new List<ThemeCatalogEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadThemeCatalogEntry(reader));
        }

        return results;
    }

    public async Task<bool> DeleteThemeCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ThemeCatalog WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Theme catalog entry deletion attempted: {EntryId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static ThemeCatalogEntry ReadThemeCatalogEntry(SqliteDataReader reader)
    {
        return new ThemeCatalogEntry
        {
            Id = reader.GetString(0),
            Label = reader.GetString(1),
            Description = reader.GetString(2),
            Keywords = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
            Weight = reader.GetInt32(4),
            Category = reader.GetString(5),
            StatAffinities = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(6)) ?? new(),
            ScenarioFitRules = reader.GetString(7),
            IsEnabled = reader.GetInt32(8) != 0,
            IsBuiltIn = reader.GetInt32(9) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(10), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(11), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Scenario Definition persistence ---

    public async Task SaveScenarioDefinitionAsync(ScenarioDefinitionEntity definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO ScenarioDefinitions (
                Id, Label, Description, Category, Weight, VariantOf, IsScenarioDefining,
                Keywords, DirectionalKeywords, StatAffinities, ScenarioFitRules, PhaseGuidance,
                IsEnabled, CreatedUtc, UpdatedUtc)
            VALUES (
                $id, $label, $description, $category, $weight, $variantOf, $isScenarioDefining,
                $keywords, $directionalKeywords, $statAffinities, $scenarioFitRules, $phaseGuidance,
                $isEnabled, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Label = $label,
                Description = $description,
                Category = $category,
                Weight = $weight,
                VariantOf = $variantOf,
                IsScenarioDefining = $isScenarioDefining,
                Keywords = $keywords,
                DirectionalKeywords = $directionalKeywords,
                StatAffinities = $statAffinities,
                ScenarioFitRules = $scenarioFitRules,
                PhaseGuidance = $phaseGuidance,
                IsEnabled = $isEnabled,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", definition.Id);
        command.Parameters.AddWithValue("$label", definition.Label);
        command.Parameters.AddWithValue("$description", definition.Description);
        command.Parameters.AddWithValue("$category", definition.Category);
        command.Parameters.AddWithValue("$weight", definition.Weight);
        command.Parameters.AddWithValue("$variantOf", definition.VariantOf ?? string.Empty);
        command.Parameters.AddWithValue("$isScenarioDefining", definition.IsScenarioDefining ? 1 : 0);
        command.Parameters.AddWithValue("$keywords", JsonSerializer.Serialize(definition.Keywords));
        command.Parameters.AddWithValue("$directionalKeywords", JsonSerializer.Serialize(definition.DirectionalKeywords));
        command.Parameters.AddWithValue("$statAffinities", JsonSerializer.Serialize(definition.StatAffinities));
        command.Parameters.AddWithValue("$scenarioFitRules", definition.ScenarioFitRules ?? string.Empty);
        command.Parameters.AddWithValue("$phaseGuidance", definition.PhaseGuidance ?? string.Empty);
        command.Parameters.AddWithValue("$isEnabled", definition.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdUtc", definition.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario definition persisted: {DefinitionId}, Label={Label}", definition.Id, definition.Label);
    }

    public async Task<ScenarioDefinitionEntity?> LoadScenarioDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Label, Description, Category, Weight, VariantOf, IsScenarioDefining,
                   Keywords, DirectionalKeywords, StatAffinities, ScenarioFitRules, PhaseGuidance,
                   IsEnabled, CreatedUtc, UpdatedUtc
            FROM ScenarioDefinitions
            WHERE Id = $id
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadScenarioDefinition(reader);
    }

    public async Task<List<ScenarioDefinitionEntity>> LoadAllScenarioDefinitionsAsync(bool includeDisabled, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = includeDisabled
            ? "SELECT Id, Label, Description, Category, Weight, VariantOf, IsScenarioDefining, Keywords, DirectionalKeywords, StatAffinities, ScenarioFitRules, PhaseGuidance, IsEnabled, CreatedUtc, UpdatedUtc FROM ScenarioDefinitions ORDER BY Label"
            : "SELECT Id, Label, Description, Category, Weight, VariantOf, IsScenarioDefining, Keywords, DirectionalKeywords, StatAffinities, ScenarioFitRules, PhaseGuidance, IsEnabled, CreatedUtc, UpdatedUtc FROM ScenarioDefinitions WHERE IsEnabled = 1 ORDER BY Label";

        var results = new List<ScenarioDefinitionEntity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadScenarioDefinition(reader));
        }

        return results;
    }

    public async Task<bool> DeleteScenarioDefinitionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ScenarioDefinitions WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        var rowsAffected = await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Scenario definition deletion attempted: {DefinitionId}, RowsAffected={RowsAffected}", id, rowsAffected);
        return rowsAffected > 0;
    }

    private static ScenarioDefinitionEntity ReadScenarioDefinition(SqliteDataReader reader)
    {
        return new ScenarioDefinitionEntity
        {
            Id = reader.GetString(0),
            Label = reader.GetString(1),
            Description = reader.GetString(2),
            Category = reader.GetString(3),
            Weight = reader.GetInt32(4),
            VariantOf = reader.GetString(5),
            IsScenarioDefining = reader.GetInt32(6) != 0,
            Keywords = JsonSerializer.Deserialize<List<string>>(reader.GetString(7)) ?? [],
            DirectionalKeywords = JsonSerializer.Deserialize<List<string>>(reader.GetString(8)) ?? [],
            StatAffinities = JsonSerializer.Deserialize<Dictionary<string, int>>(reader.GetString(9)) ?? new(StringComparer.OrdinalIgnoreCase),
            ScenarioFitRules = reader.GetString(10),
            PhaseGuidance = reader.GetString(11),
            IsEnabled = reader.GetInt32(12) != 0,
            CreatedUtc = DateTime.TryParse(reader.GetString(13), out var cre) ? cre : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(14), out var upd) ? upd : DateTime.UtcNow
        };
    }

    // --- Story Ranking persistence ---

    public async Task SaveStoryRankingAsync(StoryRankingResult ranking, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ranking);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryRankings (Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, ThemeVerificationStatusJson, GeneratedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $profileId, $themeSnapshotJson, $themeDetectionsJson, $score, $isDisqualified, $themeVerificationStatusJson, $generatedUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId, ProfileId) DO UPDATE SET
                ThemeSnapshotJson = $themeSnapshotJson,
                ThemeDetectionsJson = $themeDetectionsJson,
                Score = $score,
                IsDisqualified = $isDisqualified,
                ThemeVerificationStatusJson = $themeVerificationStatusJson,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", ranking.Id);
        command.Parameters.AddWithValue("$parsedStoryId", ranking.ParsedStoryId);
        command.Parameters.AddWithValue("$profileId", ranking.ProfileId);
        command.Parameters.AddWithValue("$themeSnapshotJson", ranking.ThemeSnapshotJson);
        command.Parameters.AddWithValue("$themeDetectionsJson", ranking.ThemeDetectionsJson);
        command.Parameters.AddWithValue("$score", ranking.Score);
        command.Parameters.AddWithValue("$isDisqualified", ranking.IsDisqualified ? 1 : 0);
        command.Parameters.AddWithValue("$themeVerificationStatusJson", ranking.ThemeVerificationStatusJson);
        command.Parameters.AddWithValue("$generatedUtc", ranking.GeneratedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("Story ranking persisted: {ParsedStoryId}, ProfileId={ProfileId}, Score={Score}, IsDisqualified={IsDisqualified}", ranking.ParsedStoryId, ranking.ProfileId, ranking.Score, ranking.IsDisqualified);
    }

    public async Task<StoryRankingResult?> LoadStoryRankingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc, ThemeVerificationStatusJson FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId LIMIT 1";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadStoryRanking(reader);
    }

    public async Task<StoryRankingResult?> LoadStoryRankingByProfileAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc, ThemeVerificationStatusJson FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId AND ProfileId = $profileId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);
        command.Parameters.AddWithValue("$profileId", profileId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadStoryRanking(reader);
    }

    public async Task<List<StoryRankingResult>> LoadStoryRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, ProfileId, ThemeSnapshotJson, ThemeDetectionsJson, Score, IsDisqualified, GeneratedUtc, UpdatedUtc, ThemeVerificationStatusJson FROM StoryRankings WHERE ParsedStoryId = $parsedStoryId ORDER BY GeneratedUtc DESC";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        var results = new List<StoryRankingResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryRanking(reader));
        }
        return results;
    }

    private static StoryRankingResult ReadStoryRanking(SqliteDataReader reader)
    {
        return new StoryRankingResult
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            ProfileId = reader.GetString(2),
            ThemeSnapshotJson = reader.GetString(3),
            ThemeDetectionsJson = reader.GetString(4),
            Score = reader.GetDouble(5),
            IsDisqualified = reader.GetInt32(6) != 0,
            GeneratedUtc = DateTime.TryParse(reader.GetString(7), out var gen) ? gen : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(8), out var upd) ? upd : DateTime.UtcNow,
            ThemeVerificationStatusJson = reader.IsDBNull(9) ? "{}" : reader.GetString(9)
        };
    }

    private static ParsedStoryRecord ReadParsedStoryRecord(SqliteDataReader reader)
    {
        var statusString = reader.GetString(9);
        _ = Enum.TryParse<ParseStatus>(statusString, ignoreCase: true, out var status);

        return new ParsedStoryRecord
        {
            Id = reader.GetString(0),
            SourceUrl = reader.GetString(1),
            SourceDomain = reader.GetString(2),
            Title = reader.IsDBNull(3) ? null : reader.GetString(3),
            Author = reader.IsDBNull(4) ? null : reader.GetString(4),
            ParsedUtc = DateTime.TryParse(reader.GetString(5), out var parsedUtc) ? parsedUtc : DateTime.UtcNow,
            PageCount = reader.GetInt32(6),
            CombinedText = reader.GetString(7),
            StructuredPayloadJson = reader.GetString(8),
            ParseStatus = status,
            DiagnosticsSummaryJson = reader.GetString(10),
            IsArchived = reader.FieldCount > 11 && !reader.IsDBNull(11) && reader.GetInt32(11) != 0
        };
    }

    // --- Story Collection operations ---

    public async Task SaveStoryCollectionAsync(StoryCollection collection, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryCollections (Id, Name, Description, CreatedUtc, UpdatedUtc)
            VALUES ($id, $name, $description, $createdUtc, $updatedUtc)
            ON CONFLICT(Id) DO UPDATE SET
                Name = $name,
                Description = $description,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", collection.Id);
        command.Parameters.AddWithValue("$name", collection.Name);
        command.Parameters.AddWithValue("$description", (object?)collection.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdUtc", collection.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoryCollection?> LoadStoryCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadStoryCollection(reader);
        }
        return null;
    }

    public async Task<List<StoryCollection>> LoadAllStoryCollectionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections ORDER BY Name ASC";

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    public async Task<bool> DeleteStoryCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Enable foreign keys for cascade
        var pragmaCmd = connection.CreateCommand();
        pragmaCmd.CommandText = "PRAGMA foreign_keys = ON";
        await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollections WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<List<StoryCollection>> SearchStoryCollectionsAsync(string query, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, Description, CreatedUtc, UpdatedUtc FROM StoryCollections
            WHERE Name LIKE $query OR Description LIKE $query
            ORDER BY Name ASC
            """;
        command.Parameters.AddWithValue("$query", $"%{query}%");

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    // --- Story Collection Membership operations ---

    public async Task SaveStoryCollectionMemberAsync(StoryCollectionMembership membership, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO StoryCollectionMembers (Id, CollectionId, ParsedStoryId, SortOrder, AddedUtc)
            VALUES ($id, $collectionId, $parsedStoryId, $sortOrder, $addedUtc)
            ON CONFLICT(CollectionId, ParsedStoryId) DO UPDATE SET
                SortOrder = $sortOrder;
            """;

        command.Parameters.AddWithValue("$id", membership.Id);
        command.Parameters.AddWithValue("$collectionId", membership.CollectionId);
        command.Parameters.AddWithValue("$parsedStoryId", membership.ParsedStoryId);
        command.Parameters.AddWithValue("$sortOrder", membership.SortOrder);
        command.Parameters.AddWithValue("$addedUtc", membership.AddedUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<StoryCollectionMembership>> LoadCollectionMembersAsync(string collectionId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, CollectionId, ParsedStoryId, SortOrder, AddedUtc
            FROM StoryCollectionMembers
            WHERE CollectionId = $collectionId
            ORDER BY SortOrder ASC
            """;
        command.Parameters.AddWithValue("$collectionId", collectionId);

        var results = new List<StoryCollectionMembership>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollectionMembership(reader));
        }
        return results;
    }

    public async Task<List<StoryCollection>> LoadCollectionsForStoryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT c.Id, c.Name, c.Description, c.CreatedUtc, c.UpdatedUtc
            FROM StoryCollections c
            INNER JOIN StoryCollectionMembers m ON c.Id = m.CollectionId
            WHERE m.ParsedStoryId = $parsedStoryId
            ORDER BY c.Name ASC
            """;
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        var results = new List<StoryCollection>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadStoryCollection(reader));
        }
        return results;
    }

    public async Task<bool> DeleteStoryCollectionMemberAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollectionMembers WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteStoryCollectionMemberByStoryAsync(string collectionId, string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM StoryCollectionMembers WHERE CollectionId = $collectionId AND ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$collectionId", collectionId);
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static StoryCollection ReadStoryCollection(SqliteDataReader reader)
    {
        return new StoryCollection
        {
            Id = reader.GetString(0),
            Name = reader.GetString(1),
            Description = reader.IsDBNull(2) ? null : reader.GetString(2),
            CreatedUtc = DateTime.TryParse(reader.GetString(3), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(4), out var updated) ? updated : DateTime.UtcNow
        };
    }

    private static StoryCollectionMembership ReadStoryCollectionMembership(SqliteDataReader reader)
    {
        return new StoryCollectionMembership
        {
            Id = reader.GetString(0),
            CollectionId = reader.GetString(1),
            ParsedStoryId = reader.GetString(2),
            SortOrder = reader.GetInt32(3),
            AddedUtc = DateTime.TryParse(reader.GetString(4), out var added) ? added : DateTime.UtcNow
        };
    }

    // ── User Story Rating ──

    public async Task SaveUserStoryRatingAsync(UserStoryRating rating, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rating);

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO UserStoryRatings (Id, ParsedStoryId, Stars, Comment, CreatedUtc, UpdatedUtc)
            VALUES ($id, $parsedStoryId, $stars, $comment, $createdUtc, $updatedUtc)
            ON CONFLICT(ParsedStoryId) DO UPDATE SET
                Stars = $stars,
                Comment = $comment,
                UpdatedUtc = $updatedUtc;
            """;

        command.Parameters.AddWithValue("$id", rating.Id);
        command.Parameters.AddWithValue("$parsedStoryId", rating.ParsedStoryId);
        command.Parameters.AddWithValue("$stars", rating.Stars.HasValue ? (object)rating.Stars.Value : DBNull.Value);
        command.Parameters.AddWithValue("$comment", rating.Comment);
        command.Parameters.AddWithValue("$createdUtc", rating.CreatedUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedUtc", DateTime.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
        _logger.LogInformation("User story rating persisted: {ParsedStoryId}, Stars={Stars}", rating.ParsedStoryId, rating.Stars);
    }

    public async Task<UserStoryRating?> LoadUserStoryRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ParsedStoryId, Stars, Comment, CreatedUtc, UpdatedUtc FROM UserStoryRatings WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new UserStoryRating
        {
            Id = reader.GetString(0),
            ParsedStoryId = reader.GetString(1),
            Stars = reader.IsDBNull(2) ? null : reader.GetInt32(2),
            Comment = reader.GetString(3),
            CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
            UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
        };
    }

    public async Task<bool> DeleteUserStoryRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM UserStoryRatings WHERE ParsedStoryId = $parsedStoryId";
        command.Parameters.AddWithValue("$parsedStoryId", parsedStoryId);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<Dictionary<string, UserStoryRating>> LoadUserStoryRatingsBatchAsync(IEnumerable<string> parsedStoryIds, CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, UserStoryRating>();
        var idList = parsedStoryIds.ToList();
        if (idList.Count == 0) return result;

        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // SQLite doesn't support array params; use IN with positional parameters
        var paramNames = idList.Select((_, idx) => $"$p{idx}").ToList();
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT Id, ParsedStoryId, Stars, Comment, CreatedUtc, UpdatedUtc FROM UserStoryRatings WHERE ParsedStoryId IN ({string.Join(",", paramNames)})";
        for (int idx = 0; idx < idList.Count; idx++)
            command.Parameters.AddWithValue($"$p{idx}", idList[idx]);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var rating = new UserStoryRating
            {
                Id = reader.GetString(0),
                ParsedStoryId = reader.GetString(1),
                Stars = reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Comment = reader.GetString(3),
                CreatedUtc = DateTime.TryParse(reader.GetString(4), out var created) ? created : DateTime.UtcNow,
                UpdatedUtc = DateTime.TryParse(reader.GetString(5), out var updated) ? updated : DateTime.UtcNow
            };
            result[rating.ParsedStoryId] = rating;
        }

        return result;
    }

    private string ResolveDatabasePath()
    {
        var connectionStringBuilder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (string.IsNullOrWhiteSpace(connectionStringBuilder.DataSource))
        {
            throw new InvalidOperationException("Persistence connection string does not contain a SQLite data source.");
        }

        return Path.GetFullPath(connectionStringBuilder.DataSource);
    }

    private static string SanitizeFileToken(string value)
    {
        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value
            .Select(ch => invalidFileNameChars.Contains(ch) ? '-' : ch)
            .ToArray())
            .Trim()
            .Replace(' ', '-');

        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            return "backup";
        }

        return sanitized.Length > 32 ? sanitized[..32] : sanitized;
    }

    // --- B-075: Steering Generation Records ---

    public async Task SaveSteeringGenerationRecordAsync(SteeringGenerationRecord record, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR REPLACE INTO SteeringGenerationRecords
                (Id, SessionId, GenerationPrompt, GenerationResponse, ParsedOptionsJson,
                 CharacterSnapshotJson, ActiveThemeId, ActiveThemeLabel, Phase,
                 ModelIdentifier, ProviderName, Temperature, TopP, MaxTokens,
                 Succeeded, ErrorMessage, SelectedDirectiveSummary,
                 StagedInteractionId, ContinuationInteractionId,
                 CreatedUtc, UpdatedUtc)
            VALUES
                (@Id, @SessionId, @GenerationPrompt, @GenerationResponse, @ParsedOptionsJson,
                 @CharacterSnapshotJson, @ActiveThemeId, @ActiveThemeLabel, @Phase,
                 @ModelIdentifier, @ProviderName, @Temperature, @TopP, @MaxTokens,
                 @Succeeded, @ErrorMessage, @SelectedDirectiveSummary,
                 @StagedInteractionId, @ContinuationInteractionId,
                 @CreatedUtc, @UpdatedUtc)
            """;
        var p = command.Parameters;
        p.AddWithValue("@Id", record.Id);
        p.AddWithValue("@SessionId", record.SessionId);
        p.AddWithValue("@GenerationPrompt", record.GenerationPrompt);
        p.AddWithValue("@GenerationResponse", record.GenerationResponse);
        p.AddWithValue("@ParsedOptionsJson", (object?)record.ParsedOptionsJson ?? DBNull.Value);
        p.AddWithValue("@CharacterSnapshotJson", (object?)record.CharacterSnapshotJson ?? DBNull.Value);
        p.AddWithValue("@ActiveThemeId", (object?)record.ActiveThemeId ?? DBNull.Value);
        p.AddWithValue("@ActiveThemeLabel", (object?)record.ActiveThemeLabel ?? DBNull.Value);
        p.AddWithValue("@Phase", (object?)record.Phase ?? DBNull.Value);
        p.AddWithValue("@ModelIdentifier", (object?)record.ModelIdentifier ?? DBNull.Value);
        p.AddWithValue("@ProviderName", (object?)record.ProviderName ?? DBNull.Value);
        p.AddWithValue("@Temperature", (object?)record.Temperature ?? DBNull.Value);
        p.AddWithValue("@TopP", (object?)record.TopP ?? DBNull.Value);
        p.AddWithValue("@MaxTokens", (object?)record.MaxTokens ?? DBNull.Value);
        p.AddWithValue("@Succeeded", record.Succeeded ? 1L : 0L);
        p.AddWithValue("@ErrorMessage", (object?)record.ErrorMessage ?? DBNull.Value);
        p.AddWithValue("@SelectedDirectiveSummary", (object?)record.SelectedDirectiveSummary ?? DBNull.Value);
        p.AddWithValue("@StagedInteractionId", (object?)record.StagedInteractionId ?? DBNull.Value);
        p.AddWithValue("@ContinuationInteractionId", (object?)record.ContinuationInteractionId ?? DBNull.Value);
        p.AddWithValue("@CreatedUtc", record.CreatedUtc.ToString("O"));
        p.AddWithValue("@UpdatedUtc", record.UpdatedUtc.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SteeringGenerationRecord?> LoadSteeringGenerationRecordAsync(string id, CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SteeringGenerationRecords WHERE Id = @Id";
        command.Parameters.AddWithValue("@Id", id);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return ReadSteeringGenerationRecord(reader);
    }

    private static SteeringGenerationRecord ReadSteeringGenerationRecord(SqliteDataReader reader)
    {
        return new SteeringGenerationRecord
        {
            Id = reader.GetString(0),
            SessionId = reader.GetString(1),
            GenerationPrompt = reader.GetString(2),
            GenerationResponse = reader.GetString(3),
            ParsedOptionsJson = reader.IsDBNull(4) ? null : reader.GetString(4),
            CharacterSnapshotJson = reader.IsDBNull(5) ? null : reader.GetString(5),
            ActiveThemeId = reader.IsDBNull(6) ? null : reader.GetString(6),
            ActiveThemeLabel = reader.IsDBNull(7) ? null : reader.GetString(7),
            Phase = reader.IsDBNull(8) ? null : reader.GetString(8),
            ModelIdentifier = reader.IsDBNull(9) ? null : reader.GetString(9),
            ProviderName = reader.IsDBNull(10) ? null : reader.GetString(10),
            Temperature = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            TopP = reader.IsDBNull(12) ? null : reader.GetDouble(12),
            MaxTokens = reader.IsDBNull(13) ? null : reader.GetInt32(13),
            Succeeded = reader.GetInt64(14) != 0,
            ErrorMessage = reader.IsDBNull(15) ? null : reader.GetString(15),
            SelectedDirectiveSummary = reader.IsDBNull(16) ? null : reader.GetString(16),
            StagedInteractionId = reader.IsDBNull(17) ? null : reader.GetString(17),
            ContinuationInteractionId = reader.IsDBNull(18) ? null : reader.GetString(18),
            CreatedUtc = DateTime.Parse(reader.GetString(19)),
            UpdatedUtc = DateTime.Parse(reader.GetString(20)),
        };
    }

    public async Task<SteeringGenerationRecord?> GetLatestSteeringGenerationRecordAsync(string sessionId)
    {
        await using var connection = new SqliteConnection(_options.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM SteeringGenerationRecords WHERE SessionId = @SessionId AND Succeeded = 1 ORDER BY CreatedUtc DESC LIMIT 1";
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return ReadSteeringGenerationRecord(reader);
    }
}
