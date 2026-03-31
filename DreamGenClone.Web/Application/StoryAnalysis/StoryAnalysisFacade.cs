using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Models;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;

namespace DreamGenClone.Web.Application.StoryAnalysis;

public sealed class StoryAnalysisFacade
{
    private readonly IStorySummaryService _summaryService;
    private readonly IStoryAnalysisService _analysisService;
    private readonly IThemePreferenceService _themeService;
    private readonly IRankingProfileService _profileService;
    private readonly IStoryRankingService _rankingService;
    private readonly ISqlitePersistence _persistence;

    public StoryAnalysisFacade(
        IStorySummaryService summaryService,
        IStoryAnalysisService analysisService,
        IThemePreferenceService themeService,
        IRankingProfileService profileService,
        IStoryRankingService rankingService,
        ISqlitePersistence persistence)
    {
        _summaryService = summaryService;
        _analysisService = analysisService;
        _themeService = themeService;
        _profileService = profileService;
        _rankingService = rankingService;
        _persistence = persistence;
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
    public Task<RankingProfile> CreateProfileAsync(string name, CancellationToken cancellationToken = default)
        => _profileService.CreateAsync(name, cancellationToken);

    public Task<List<RankingProfile>> ListProfilesAsync(CancellationToken cancellationToken = default)
        => _profileService.ListAsync(cancellationToken);

    public Task<RankingProfile?> GetProfileAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.GetAsync(id, cancellationToken);

    public Task<RankingProfile?> UpdateProfileAsync(string id, string name, CancellationToken cancellationToken = default)
        => _profileService.UpdateAsync(id, name, cancellationToken);

    public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.DeleteAsync(id, cancellationToken);

    public Task SetProfileDefaultAsync(string id, CancellationToken cancellationToken = default)
        => _profileService.SetDefaultAsync(id, cancellationToken);

    public Task<RankingProfile?> GetDefaultProfileAsync(CancellationToken cancellationToken = default)
        => _profileService.GetDefaultAsync(cancellationToken);

    // Theme Preferences
    public Task<ThemePreference> CreateThemeAsync(string profileId, string name, string description, ThemeTier tier, CancellationToken cancellationToken = default)
        => _themeService.CreateAsync(profileId, name, description, tier, cancellationToken);

    public Task<List<ThemePreference>> ListThemesAsync(CancellationToken cancellationToken = default)
        => _themeService.ListAsync(cancellationToken);

    public Task<List<ThemePreference>> ListThemesByProfileAsync(string profileId, CancellationToken cancellationToken = default)
        => _themeService.ListByProfileAsync(profileId, cancellationToken);

    public Task<ThemePreference?> UpdateThemeAsync(string id, string name, string description, ThemeTier tier, CancellationToken cancellationToken = default)
        => _themeService.UpdateAsync(id, name, description, tier, cancellationToken);

    public Task<bool> DeleteThemeAsync(string id, CancellationToken cancellationToken = default)
        => _themeService.DeleteAsync(id, cancellationToken);

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
}
