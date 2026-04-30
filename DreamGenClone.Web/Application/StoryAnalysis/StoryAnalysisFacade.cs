using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;

namespace DreamGenClone.Web.Application.StoryAnalysis;

public sealed class StoryAnalysisFacade
{
    private readonly IStorySummaryService _summaryService;
    private readonly IStoryAnalysisService _analysisService;
    private readonly IThemePreferenceService _themeService;
    private readonly IThemeProfileService _profileService;
    private readonly IIntensityProfileService _intensityProfileService;
    private readonly ISteeringProfileService _steeringProfileService;
    private readonly IBaseStatProfileService _baseStatProfileService;
    private readonly IStatWillingnessProfileService _statWillingnessProfileService;
    private readonly INarrativeGateProfileService _narrativeGateProfileService;
    private readonly IHusbandAwarenessProfileService _husbandAwarenessProfileService;
    private readonly IBackgroundCharacterProfileService _backgroundCharacterProfileService;
    private readonly IRoleDefinitionService _roleDefinitionService;
    private readonly IStoryRankingService _rankingService;
    private readonly IThemeCatalogService _themeCatalogService;
    private readonly IScenarioDefinitionService _scenarioDefinitionService;
    private readonly IThemeDefinitionService _themeDefinitionService;
    private readonly ICharacterStatPresetImportService _characterStatPresetImportService;
    private readonly IStatKeywordCategoryService _statKeywordCategoryService;
    private readonly IRPThemeService _rpThemeService;
    private readonly ISqlitePersistence _persistence;
    private readonly IScenarioEngineSettingsRepository _engineSettingsRepository;

    public StoryAnalysisFacade(
        IStorySummaryService summaryService,
        IStoryAnalysisService analysisService,
        IThemePreferenceService themeService,
        IThemeProfileService profileService,
        IIntensityProfileService toneProfileService,
        ISteeringProfileService styleProfileService,
        IBaseStatProfileService baseStatProfileService,
        IStatWillingnessProfileService statWillingnessProfileService,
        INarrativeGateProfileService narrativeGateProfileService,
        IHusbandAwarenessProfileService husbandAwarenessProfileService,
        IBackgroundCharacterProfileService backgroundCharacterProfileService,
        IRoleDefinitionService roleDefinitionService,
        IStoryRankingService rankingService,
        IThemeCatalogService themeCatalogService,
        IScenarioDefinitionService scenarioDefinitionService,
        IThemeDefinitionService themeDefinitionService,
        ICharacterStatPresetImportService characterStatPresetImportService,
        IStatKeywordCategoryService statKeywordCategoryService,
        IRPThemeService rpThemeService,
        ISqlitePersistence persistence,
        IScenarioEngineSettingsRepository engineSettingsRepository)
    {
        _summaryService = summaryService;
        _analysisService = analysisService;
        _themeService = themeService;
        _profileService = profileService;
        _intensityProfileService = toneProfileService;
        _steeringProfileService = styleProfileService;
        _baseStatProfileService = baseStatProfileService;
        _statWillingnessProfileService = statWillingnessProfileService;
        _narrativeGateProfileService = narrativeGateProfileService;
        _husbandAwarenessProfileService = husbandAwarenessProfileService;
        _backgroundCharacterProfileService = backgroundCharacterProfileService;
        _roleDefinitionService = roleDefinitionService;
        _rankingService = rankingService;
        _themeCatalogService = themeCatalogService;
        _scenarioDefinitionService = scenarioDefinitionService;
        _themeDefinitionService = themeDefinitionService;
        _characterStatPresetImportService = characterStatPresetImportService;
        _statKeywordCategoryService = statKeywordCategoryService;
        _rpThemeService = rpThemeService;
        _persistence = persistence;
        _engineSettingsRepository = engineSettingsRepository;
    }

    // Summary
    public Task<SummarizeResult> SummarizeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _summaryService.SummarizeAsync(parsedStoryId, cancellationToken);

    public Task<StorySummary?> GetSummaryAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _summaryService.GetSummaryAsync(parsedStoryId, cancellationToken);

    // Analysis
    public Task<AnalyzeResult> AnalyzeAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _analysisService.AnalyzeAsync(parsedStoryId, cancellationToken);

    public Task<StoryAnalysisResult?> GetAnalysisAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _analysisService.GetAnalysisAsync(parsedStoryId, cancellationToken);

    // Ranking Profiles
    public Task<ThemeProfile> CreateProfileAsync(string name, CancellationToken cancellationToken = default)
        => _profileService.CreateAsync(name, cancellationToken);

    public Task<List<ThemeProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
        => _profileService.ListAsync(cancellationToken);

    public Task<ThemeProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.GetAsync(id, cancellationToken);

    public Task<ThemeProfile?> UpdateProfileAsync(string id, string name, CancellationToken cancellationToken = default)
        => _profileService.UpdateAsync(id, name, cancellationToken);

    public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.DeleteAsync(id, cancellationToken);

    public Task SetProfileDefaultAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.SetDefaultAsync(id, cancellationToken);

    public Task<ThemeProfile?> GetDefaultProfileAsync(CancellationToken cancellationToken = default)
        => _profileService.GetDefaultAsync(cancellationToken);

    // Theme Preferences
    public Task<ThemePreference> CreateThemeAsync(string profileId, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
        => _themeService.CreateAsync(profileId, name, description, tier, catalogId, cancellationToken);

    public Task<List<ThemePreference>> ListThemesAsync(CancellationToken cancellationToken = default)
        => _themeService.ListAsync(cancellationToken);

    public Task<List<ThemePreference>> ListThemesByProfileAsync(string profileId, CancellationToken cancellationToken = default)
        => _themeService.ListByProfileAsync(profileId, cancellationToken);

    public Task<ThemePreference?> UpdateThemeAsync(string id, string name, string description, ThemeTier tier, string? catalogId = null, CancellationToken cancellationToken = default)
        => _themeService.UpdateAsync(id, name, description, tier, catalogId, cancellationToken);

    public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
        => _themeService.DeleteAsync(id, cancellationToken);

    // Finishing Move Matrix Rows (global base table)
    public Task<IReadOnlyList<RPFinishingMoveMatrixRow>> ListFinishingMoveMatrixRowsAsync(CancellationToken cancellationToken = default)
        => _rpThemeService.ListFinishingMoveMatrixRowsAsync(cancellationToken);

    public Task<RPFinishingMoveMatrixRow> SaveFinishingMoveMatrixRowAsync(RPFinishingMoveMatrixRow row, CancellationToken cancellationToken = default)
        => _rpThemeService.SaveFinishingMoveMatrixRowAsync(row, cancellationToken);

    public Task<bool> DeleteFinishingMoveMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
        => _rpThemeService.DeleteFinishingMoveMatrixRowAsync(rowId, cancellationToken);

    public Task<int> ImportFinishingMoveMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
        => _rpThemeService.ImportFinishingMoveMatrixRowsFromJsonAsync(json, replaceExisting, cancellationToken);

    // Steer Position Matrix Rows (global base table)
    public Task<IReadOnlyList<RPSteerPositionMatrixRow>> ListSteerPositionMatrixRowsAsync(CancellationToken cancellationToken = default)
        => _rpThemeService.ListSteerPositionMatrixRowsAsync(cancellationToken);

    public Task<RPSteerPositionMatrixRow> SaveSteerPositionMatrixRowAsync(RPSteerPositionMatrixRow row, CancellationToken cancellationToken = default)
        => _rpThemeService.SaveSteerPositionMatrixRowAsync(row, cancellationToken);

    public Task<bool> DeleteSteerPositionMatrixRowAsync(string rowId, CancellationToken cancellationToken = default)
        => _rpThemeService.DeleteSteerPositionMatrixRowAsync(rowId, cancellationToken);

    public Task<int> ImportSteerPositionMatrixRowsFromJsonAsync(
        string json,
        bool replaceExisting = false,
        CancellationToken cancellationToken = default)
        => _rpThemeService.ImportSteerPositionMatrixRowsFromJsonAsync(json, replaceExisting, cancellationToken);

    // Intensity Profiles
    public Task<IntensityProfile> CreateIntensityProfileAsync(
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        string sceneDirective = "",
        CancellationToken cancellationToken = default)
        => _intensityProfileService.CreateAsync(
            name,
            description,
            intensity,
            buildUpPhaseOffset,
            committedPhaseOffset,
            approachingPhaseOffset,
            climaxPhaseOffset,
            resetPhaseOffset,
            sceneDirective,
            cancellationToken);

    public Task<List<IntensityProfile>> ListIntensityProfilesAsync(CancellationToken cancellationToken = default)
        => _intensityProfileService.ListAsync(cancellationToken);

    public Task<IntensityProfile?> GetIntensityProfileAsync(string id, CancellationToken cancellationToken = default)
        => _intensityProfileService.GetAsync(id, cancellationToken);

    public Task<IntensityProfile?> UpdateIntensityProfileAsync(
        string id,
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        string sceneDirective = "",
        CancellationToken cancellationToken = default)
        => _intensityProfileService.UpdateAsync(
            id,
            name,
            description,
            intensity,
            buildUpPhaseOffset,
            committedPhaseOffset,
            approachingPhaseOffset,
            climaxPhaseOffset,
            resetPhaseOffset,
            sceneDirective,
            cancellationToken);

    public Task<bool> DeleteIntensityProfileAsync(string id, CancellationToken cancellationToken = default)
        => _intensityProfileService.DeleteAsync(id, cancellationToken);

    // Steering Profiles
    public Task<SteeringProfile> CreateSteeringProfileAsync(string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
        => _steeringProfileService.CreateAsync(name, description, example, ruleOfThumb, themeAffinities, escalatingThemeIds, statBias, cancellationToken);

    public Task<List<SteeringProfile>> ListSteeringProfilesAsync(CancellationToken cancellationToken = default)
        => _steeringProfileService.ListAsync(cancellationToken);

    public Task<SteeringProfile?> GetSteeringProfileAsync(string id, CancellationToken cancellationToken = default)
        => _steeringProfileService.GetAsync(id, cancellationToken);

    public Task<SteeringProfile?> UpdateSteeringProfileAsync(string id, string name, string description, string example, string ruleOfThumb, Dictionary<string, int>? themeAffinities = null, List<string>? escalatingThemeIds = null, Dictionary<string, int>? statBias = null, CancellationToken cancellationToken = default)
        => _steeringProfileService.UpdateAsync(id, name, description, example, ruleOfThumb, themeAffinities, escalatingThemeIds, statBias, cancellationToken);

    public Task<bool> DeleteSteeringProfileAsync(string id, CancellationToken cancellationToken = default)
        => _steeringProfileService.DeleteAsync(id, cancellationToken);

    // Base Stat Profiles
    public Task<BaseStatProfile> CreateBaseStatProfileAsync(string name, string description, IReadOnlyDictionary<string, int> defaultStats, string targetGender, string targetRole, CancellationToken cancellationToken = default)
        => _baseStatProfileService.CreateAsync(name, description, defaultStats, targetGender, targetRole, cancellationToken);

    public Task<List<BaseStatProfile>> ListBaseStatProfilesAsync(CancellationToken cancellationToken = default)
        => _baseStatProfileService.ListAsync(cancellationToken);

    public Task<BaseStatProfile?> GetBaseStatProfileAsync(string id, CancellationToken cancellationToken = default)
        => _baseStatProfileService.GetAsync(id, cancellationToken);

    public Task<BaseStatProfile?> UpdateBaseStatProfileAsync(string id, string name, string description, IReadOnlyDictionary<string, int> defaultStats, string targetGender, string targetRole, CancellationToken cancellationToken = default)
        => _baseStatProfileService.UpdateAsync(id, name, description, defaultStats, targetGender, targetRole, cancellationToken);

    public Task<bool> DeleteBaseStatProfileAsync(string id, CancellationToken cancellationToken = default)
        => _baseStatProfileService.DeleteAsync(id, cancellationToken);

    public Task<CharacterStatPresetImportResult> ImportCharacterStatPresetsAsync(CancellationToken cancellationToken = default)
        => _characterStatPresetImportService.ImportAsync(cancellationToken);

    // Stat Keyword Categories
    public Task<List<StatKeywordCategory>> ListStatKeywordCategoriesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
        => _statKeywordCategoryService.ListAsync(includeDisabled, cancellationToken);

    public Task<StatKeywordCategory?> GetStatKeywordCategoryAsync(string id, CancellationToken cancellationToken = default)
        => _statKeywordCategoryService.GetAsync(id, cancellationToken);

    public Task<StatKeywordCategory> SaveStatKeywordCategoryAsync(StatKeywordCategory category, CancellationToken cancellationToken = default)
        => _statKeywordCategoryService.SaveAsync(category, cancellationToken);

    public Task<bool> DeleteStatKeywordCategoryAsync(string id, CancellationToken cancellationToken = default)
        => _statKeywordCategoryService.DeleteAsync(id, cancellationToken);

    // Stat Willingness Profiles
    public Task<StatWillingnessProfile> SaveStatWillingnessProfileAsync(StatWillingnessProfile profile, CancellationToken cancellationToken = default)
        => _statWillingnessProfileService.SaveAsync(profile, cancellationToken);

    public Task<List<StatWillingnessProfile>> ListStatWillingnessProfilesAsync(CancellationToken cancellationToken = default)
        => _statWillingnessProfileService.ListAsync(cancellationToken);

    public Task<StatWillingnessProfile?> GetStatWillingnessProfileAsync(string id, CancellationToken cancellationToken = default)
        => _statWillingnessProfileService.GetAsync(id, cancellationToken);

    public Task<StatWillingnessProfile?> GetDefaultStatWillingnessProfileAsync(CancellationToken cancellationToken = default)
        => _statWillingnessProfileService.GetDefaultAsync(cancellationToken);

    public Task<bool> DeleteStatWillingnessProfileAsync(string id, CancellationToken cancellationToken = default)
        => _statWillingnessProfileService.DeleteAsync(id, cancellationToken);

    // Narrative Gate Profiles
    public Task<NarrativeGateProfile> SaveNarrativeGateProfileAsync(NarrativeGateProfile profile, CancellationToken cancellationToken = default)
        => _narrativeGateProfileService.SaveAsync(profile, cancellationToken);

    public Task<List<NarrativeGateProfile>> ListNarrativeGateProfilesAsync(CancellationToken cancellationToken = default)
        => _narrativeGateProfileService.ListAsync(cancellationToken);

    public Task<NarrativeGateProfile?> GetNarrativeGateProfileAsync(string id, CancellationToken cancellationToken = default)
        => _narrativeGateProfileService.GetAsync(id, cancellationToken);

    public Task<NarrativeGateProfile?> GetDefaultNarrativeGateProfileAsync(CancellationToken cancellationToken = default)
        => _narrativeGateProfileService.GetDefaultAsync(cancellationToken);

    public Task<bool> DeleteNarrativeGateProfileAsync(string id, CancellationToken cancellationToken = default)
        => _narrativeGateProfileService.DeleteAsync(id, cancellationToken);

    // Husband Awareness Profiles
    public Task<HusbandAwarenessProfile> SaveHusbandAwarenessProfileAsync(HusbandAwarenessProfile profile, CancellationToken cancellationToken = default)
        => _husbandAwarenessProfileService.SaveAsync(profile, cancellationToken);

    public Task<List<HusbandAwarenessProfile>> ListHusbandAwarenessProfilesAsync(CancellationToken cancellationToken = default)
        => _husbandAwarenessProfileService.ListAsync(cancellationToken);

    public Task<HusbandAwarenessProfile?> GetHusbandAwarenessProfileAsync(string id, CancellationToken cancellationToken = default)
        => _husbandAwarenessProfileService.GetAsync(id, cancellationToken);

    public Task<bool> DeleteHusbandAwarenessProfileAsync(string id, CancellationToken cancellationToken = default)
        => _husbandAwarenessProfileService.DeleteAsync(id, cancellationToken);

    // Background Character Profiles
    public Task<BackgroundCharacterProfile> SaveBackgroundCharacterProfileAsync(BackgroundCharacterProfile profile, CancellationToken cancellationToken = default)
        => _backgroundCharacterProfileService.SaveAsync(profile, cancellationToken);

    public Task<List<BackgroundCharacterProfile>> ListBackgroundCharacterProfilesAsync(CancellationToken cancellationToken = default)
        => _backgroundCharacterProfileService.ListAsync(cancellationToken);

    public Task<BackgroundCharacterProfile?> GetBackgroundCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
        => _backgroundCharacterProfileService.GetAsync(id, cancellationToken);

    public Task<bool> DeleteBackgroundCharacterProfileAsync(string id, CancellationToken cancellationToken = default)
        => _backgroundCharacterProfileService.DeleteAsync(id, cancellationToken);

    // Role Definitions
    public Task<RoleDefinition> SaveRoleDefinitionAsync(RoleDefinition roleDefinition, CancellationToken cancellationToken = default)
        => _roleDefinitionService.SaveAsync(roleDefinition, cancellationToken);

    public Task<List<RoleDefinition>> ListRoleDefinitionsAsync(CancellationToken cancellationToken = default)
        => _roleDefinitionService.ListAsync(cancellationToken);

    public Task<RoleDefinition?> GetRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
        => _roleDefinitionService.GetAsync(id, cancellationToken);

    public Task<bool> DeleteRoleDefinitionAsync(string id, CancellationToken cancellationToken = default)
        => _roleDefinitionService.DeleteAsync(id, cancellationToken);

    // Ranking
    public Task<ThemeRankResult> RankAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
        => _rankingService.RankAsync(parsedStoryId, profileId, cancellationToken);

    public Task<StoryRankingResult?> GetRankingAsync(string parsedStoryId, string profileId, CancellationToken cancellationToken = default)
        => _rankingService.GetRankingAsync(parsedStoryId, profileId, cancellationToken);

    public Task<List<StoryRankingResult>> GetRankingsAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _rankingService.GetRankingsAsync(parsedStoryId, cancellationToken);

    // User Story Rating
    public Task SaveUserRatingAsync(UserStoryRating rating, CancellationToken cancellationToken = default)
        => _persistence.SaveUserStoryRatingAsync(rating, cancellationToken);

    public Task<UserStoryRating?> GetUserRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _persistence.LoadUserStoryRatingAsync(parsedStoryId, cancellationToken);

    public Task<bool> DeleteUserRatingAsync(string parsedStoryId, CancellationToken cancellationToken = default)
        => _persistence.DeleteUserStoryRatingAsync(parsedStoryId, cancellationToken);

    public Task<Dictionary<string, UserStoryRating>> GetUserRatingsBatchAsync(IEnumerable<string> parsedStoryIds, CancellationToken cancellationToken = default)
        => _persistence.LoadUserStoryRatingsBatchAsync(parsedStoryIds, cancellationToken);

    // Theme Verification
    public async Task UpdateThemeVerificationAsync(StoryRankingResult ranking, CancellationToken cancellationToken = default)
    {
        await _persistence.SaveStoryRankingAsync(ranking, cancellationToken);
    }

    // Combined Text
    public Task<bool> UpdateCombinedTextAsync(string id, string combinedText, CancellationToken cancellationToken = default)
        => _persistence.UpdateCombinedTextAsync(id, combinedText, cancellationToken);

    // Theme Catalog
    public Task<IReadOnlyList<ThemeCatalogEntry>> ListCatalogEntriesAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
        => _themeCatalogService.GetAllAsync(includeDisabled, cancellationToken);

    public Task SaveCatalogEntryAsync(ThemeCatalogEntry entry, CancellationToken cancellationToken = default)
        => _themeCatalogService.SaveAsync(entry, cancellationToken);

    public Task DeleteCatalogEntryAsync(string id, CancellationToken cancellationToken = default)
        => _themeCatalogService.DeleteAsync(id, cancellationToken);

    // Scenario Definitions
    public Task<IReadOnlyList<ScenarioDefinitionEntity>> ListScenarioDefinitionsAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
        => _scenarioDefinitionService.GetAllAsync(includeDisabled, cancellationToken);

    public Task SaveScenarioDefinitionAsync(ScenarioDefinitionEntity definition, CancellationToken cancellationToken = default)
        => _scenarioDefinitionService.SaveAsync(definition, cancellationToken);

    public Task DeleteScenarioDefinitionAsync(string id, CancellationToken cancellationToken = default)
        => _scenarioDefinitionService.DeleteAsync(id, cancellationToken);

    public Task<IReadOnlyList<ThemeDefinitionDocument>> ListThemeDefinitionsAsync(CancellationToken cancellationToken = default)
        => _themeDefinitionService.LoadAllAsync(cancellationToken);

    // Scenario Engine Settings
    public Task<DreamGenClone.Domain.RolePlay.ScenarioEngineSettings> LoadScenarioEngineSettingsAsync(CancellationToken cancellationToken = default)
        => _engineSettingsRepository.LoadAsync(cancellationToken);

    public Task SaveScenarioEngineSettingsAsync(DreamGenClone.Domain.RolePlay.ScenarioEngineSettings settings, CancellationToken cancellationToken = default)
        => _engineSettingsRepository.SaveAsync(settings, cancellationToken);
}
