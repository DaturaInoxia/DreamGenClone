using DreamGenClone.Application.RolePlay;
using DreamGenClone.Domain.RolePlay;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.RolePlay;

public sealed class FinishingMoveMatrixSeedService
{
    // Three canonical rows — one per EscalationTier.
    private sealed record SeedRow(
        string Tier,
        string PrimaryLocationsCsv,
        string SecondaryLocationsCsv,
        string WifeBehaviorModifier,
        string Receptivity,
        string OtherManBehaviorModifier);

    private static readonly SeedRow[] SeedRows =
    [
        new("Low",
            "Creampie,On Pussy,On Stomach",
            "On Tits,On Back",
            "Enthusiastic, she welcomes the finish.",
            "Enthusiastic",
            "Asks and follows her cue where possible."),
        new("Medium",
            "In Mouth,Facial Open Mouth,On Tits,On Face",
            "Creampie,On Ass,On Back",
            "Willing and cooperative.",
            "Accepting",
            "Leads decisively while reading her response."),
        new("High",
            "Facial Open Mouth,In Mouth,In Ass,On Face",
            "Creampie,On Ass",
            "He commands, she has no say.",
            "Enduring",
            "Commands and controls the finish without asking."),
    ];

    // Stable IDs for the three canonical rows so ON CONFLICT(Id) can upsert safely.
    private static string RowId(string tier) => $"seed-finish-matrix-{tier.ToLowerInvariant()}";

    private readonly IRPThemeService _rpThemeService;
    private readonly ILogger<FinishingMoveMatrixSeedService> _logger;

    public FinishingMoveMatrixSeedService(IRPThemeService rpThemeService, ILogger<FinishingMoveMatrixSeedService> logger)
    {
        _rpThemeService = rpThemeService;
        _logger = logger;
    }

    public async Task SeedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var profileId = IRPThemeService.GlobalThemeLibraryProfileId;
        var profile = await _rpThemeService.GetProfileAsync(profileId, cancellationToken);
        if (profile is null)
        {
            await _rpThemeService.SaveProfileAsync(new RPThemeProfile
            {
                Id = profileId,
                Name = "Global Theme Library",
                Description = "Shared RP theme definitions used across profiles.",
                IsDefault = false
            }, cancellationToken);
        }

        var existing = await _rpThemeService.ListFinishingMoveMatrixRowsAsync(cancellationToken);

        if (existing.Count > 0)
        {
            if (existing.Count > 3)
            {
                // Legacy 27-row install: band-based rows cannot be updated in-place because multiple
                // rows share the same derived tier, which would violate the UNIQUE constraint when
                // SaveFinishingMoveMatrixRowAsync sets all three band columns to the tier name.
                // Clear all legacy rows and fall through to the fresh 3-row seed below.
                foreach (var r in existing)
                    await _rpThemeService.DeleteFinishingMoveMatrixRowAsync(r.Id, cancellationToken);
                _logger.LogInformation("Cleared {Count} legacy band-based finishing move rows; reseeding with tier-based canonical rows.", existing.Count);
            }
            else
            {
                // Already in the 3-row tier format — update EscalationTier if any row drifted.
                await UpdateExistingTiersAsync(existing, cancellationToken);
                return;
            }
        }

        // Fresh install: seed three canonical tier rows.
        var sortOrder = 0;
        foreach (var seed in SeedRows)
        {
            await _rpThemeService.SaveFinishingMoveMatrixRowAsync(new RPFinishingMoveMatrixRow
            {
                Id = RowId(seed.Tier),
                EscalationTier = seed.Tier,
                PrimaryLocations = ParseCsv(seed.PrimaryLocationsCsv),
                SecondaryLocations = ParseCsv(seed.SecondaryLocationsCsv),
                ExcludedLocations = [],
                WifeReceptivity = seed.Receptivity,
                WifeBehaviorModifier = seed.WifeBehaviorModifier,
                OtherManBehaviorModifier = seed.OtherManBehaviorModifier,
                TransitionInstruction = "If current positioning does not allow the finish location, include an explicit repositioning beat before the finish.",
                SortOrder = sortOrder++,
                IsEnabled = true
            }, cancellationToken);
        }

        _logger.LogInformation("Seeded finishing move matrix defaults: {Count} tier-based rows.", SeedRows.Length);
    }

    private async Task UpdateExistingTiersAsync(
        IReadOnlyList<RPFinishingMoveMatrixRow> existing,
        CancellationToken cancellationToken)
    {
        var updated = 0;
        foreach (var row in existing)
        {
            var correctTier = DeriveRowTier(row.WifeBehaviorModifier);
            if (string.Equals(row.EscalationTier, correctTier, StringComparison.OrdinalIgnoreCase)) continue;
            row.EscalationTier = correctTier;
            await _rpThemeService.SaveFinishingMoveMatrixRowAsync(row, cancellationToken);
            updated++;
        }
        if (updated > 0)
            _logger.LogInformation("Updated EscalationTier on {Count} finishing move matrix row(s).", updated);
    }

    private static string DeriveRowTier(string wifeBehaviorModifier) => wifeBehaviorModifier switch
    {
        "Enthusiastic, she asks" or "Enthusiastic, mutual" or "Willing, eager" or
        "Willing but reserved" or "Comfortable" or "He directs, she agrees" or
        "Cooperative" or "Willing to please" or "Enthusiastic, she welcomes the finish." => "Low",

        "He commands, she's eager" or "Accepting" or "He decides" or
        "Reluctant, prefers control" or "Hesitant" or "Uncomfortable" or
        "Accommodating" or "Willing and cooperative." => "Medium",

        _ => "High"
    };

    private static List<string> ParseCsv(string csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
}
