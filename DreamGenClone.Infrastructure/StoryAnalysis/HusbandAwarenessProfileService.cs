using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class HusbandAwarenessProfileService : IHusbandAwarenessProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<HusbandAwarenessProfileService> _logger;

    public HusbandAwarenessProfileService(ISqlitePersistence persistence, ILogger<HusbandAwarenessProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<HusbandAwarenessProfile> SaveAsync(HusbandAwarenessProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await EnsureDefaultsAsync(cancellationToken);

        profile.Name = (profile.Name ?? string.Empty).Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.Notes = (profile.Notes ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Husband awareness profile name is required.", nameof(profile));
        }

        var existing = await _persistence.LoadAllHusbandAwarenessProfilesAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Husband awareness profile name already exists.");
        }

        profile.AwarenessLevel = Math.Clamp(profile.AwarenessLevel, 0, 100);
        profile.AcceptanceLevel = Math.Clamp(profile.AcceptanceLevel, 0, 100);
        profile.VoyeurismLevel = Math.Clamp(profile.VoyeurismLevel, 0, 100);
        profile.ParticipationLevel = Math.Clamp(profile.ParticipationLevel, 0, 100);
        profile.HumiliationDesire = Math.Clamp(profile.HumiliationDesire, 0, 100);
        profile.EncouragementLevel = Math.Clamp(profile.EncouragementLevel, 0, 100);
        profile.RiskTolerance = Math.Clamp(profile.RiskTolerance, 0, 100);

        profile.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveHusbandAwarenessProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Husband awareness profile saved: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<HusbandAwarenessProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadAllHusbandAwarenessProfilesAsync(cancellationToken);
    }

    public Task<HusbandAwarenessProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadHusbandAwarenessProfileAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteHusbandAwarenessProfileAsync(id, cancellationToken);

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _persistence.LoadAllHusbandAwarenessProfilesAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var defaults = new[]
        {
            new HusbandAwarenessProfile
            {
                Name = "Oblivious Husband",
                Description = "Unaware and non-participatory baseline.",
                AwarenessLevel = 10,
                AcceptanceLevel = 20,
                VoyeurismLevel = 5,
                ParticipationLevel = 0,
                HumiliationDesire = 0,
                EncouragementLevel = 5,
                RiskTolerance = 10,
                Notes = "Trusts implicitly and avoids inquiry.",
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new HusbandAwarenessProfile
            {
                Name = "Curious Observer",
                Description = "Aware and interested but mostly observational.",
                AwarenessLevel = 85,
                AcceptanceLevel = 70,
                VoyeurismLevel = 80,
                ParticipationLevel = 20,
                HumiliationDesire = 20,
                EncouragementLevel = 45,
                RiskTolerance = 40,
                Notes = "Wants details and occasional observation.",
                CreatedUtc = now,
                UpdatedUtc = now
            },
            new HusbandAwarenessProfile
            {
                Name = "Proud Hotwife Partner",
                Description = "High acceptance with enthusiastic facilitation.",
                AwarenessLevel = 95,
                AcceptanceLevel = 90,
                VoyeurismLevel = 85,
                ParticipationLevel = 70,
                HumiliationDesire = 10,
                EncouragementLevel = 80,
                RiskTolerance = 65,
                Notes = "Supportive and actively participatory framing.",
                CreatedUtc = now,
                UpdatedUtc = now
            }
        };

        foreach (var profile in defaults)
        {
            await _persistence.SaveHusbandAwarenessProfileAsync(profile, cancellationToken);
        }

        _logger.LogInformation("Seeded default husband awareness profiles.");
    }
}
