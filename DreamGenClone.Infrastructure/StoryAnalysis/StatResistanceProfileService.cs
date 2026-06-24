using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class StatResistanceProfileService : IStatResistanceProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<StatResistanceProfileService> _logger;

    public StatResistanceProfileService(ISqlitePersistence persistence, ILogger<StatResistanceProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<StatResistanceProfile> SaveAsync(StatResistanceProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await EnsureDefaultsAsync(cancellationToken);

        profile.Name = profile.Name.Trim();
        profile.Description = (profile.Description ?? string.Empty).Trim();
        profile.TargetStatName = string.IsNullOrWhiteSpace(profile.TargetStatName) ? "Loyalty" : profile.TargetStatName.Trim();
        profile.Thresholds = NormalizeThresholds(profile.Thresholds);

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new ArgumentException("Resistance profile name is required.", nameof(profile));
        }

        ValidateCoverage(profile.Thresholds);

        var existing = await _persistence.LoadAllStatResistanceProfilesAsync(cancellationToken);
        if (existing.Any(x => !string.Equals(x.Id, profile.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Resistance profile name already exists.");
        }

        profile.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString();
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveStatResistanceProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Stat resistance profile saved: {ProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public async Task<List<StatResistanceProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadAllStatResistanceProfilesAsync(cancellationToken);
    }

    public Task<StatResistanceProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.LoadStatResistanceProfileAsync(id, cancellationToken);

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        => _persistence.DeleteStatResistanceProfileAsync(id, cancellationToken);

    public async Task<StatResistanceProfile?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await EnsureDefaultsAsync(cancellationToken);
        return await _persistence.LoadDefaultStatResistanceProfileAsync(cancellationToken);
    }

    private async Task EnsureDefaultsAsync(CancellationToken cancellationToken)
    {
        var existing = await _persistence.LoadAllStatResistanceProfilesAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var seeded = new StatResistanceProfile
        {
            Name = "Married Woman Resistance",
            Description = "Default Loyalty-driven resistance mapping. Firm boundaries at high loyalty; permissive at low loyalty.",
            TargetStatName = "Loyalty",
            IsDefault = true,
            CreatedUtc = now,
            UpdatedUtc = now,
            Thresholds =
            [
                new() { SortOrder = 1, MinValue = 0, MaxValue = 5, ResistanceLevel = "No Resistance", Description = "No loyalty to the marriage — infidelity is not resisted at all.", PromptDirective = "She has no loyalty anchoring her to her marriage. She does not resist transgression and acts without guilt. Write her as fully willing and uninhibited in engagement with the other man.", ExampleScenarios = ["openly pursuing the affair", "no hesitation or guilt"] },
                new() { SortOrder = 2, MinValue = 6, MaxValue = 10, ResistanceLevel = "Token Resistance", Description = "Minimal resistance — she may briefly protest but gives in almost immediately.", PromptDirective = "She has almost no loyalty. She may voice a token objection but it is performative only — she does not mean it and will be easily overcome. Do not have her sustain resistance.", ExampleScenarios = ["a single 'we shouldn't' then compliance", "token guilt that passes quickly"] },
                new() { SortOrder = 3, MinValue = 11, MaxValue = 15, ResistanceLevel = "Weak Resistance", Description = "Weak resistance — protests briefly but yields quickly under any pressure.", PromptDirective = "She has weak loyalty. She protests briefly but yields quickly under any pressure or persistence. Her resistance is a formality, not a genuine boundary.", ExampleScenarios = ["brief objection then giving in", "looking away but not stopping"] },
                new() { SortOrder = 4, MinValue = 16, MaxValue = 20, ResistanceLevel = "Pliable Resistance", Description = "Pliable — resists verbally but body language invites continuation.", PromptDirective = "She has low loyalty. She resists verbally but her body language and hesitation invite continuation. She will fold under moderate persistence. Write her protests as half-hearted.", ExampleScenarios = ["saying no while leaning in", "weak objections that fade"] },
                new() { SortOrder = 5, MinValue = 21, MaxValue = 25, ResistanceLevel = "Hesitant", Description = "Hesitant — torn but drifting toward transgression.", PromptDirective = "She has below-average loyalty. She is torn between what she knows is wrong and what she wants. Her resistance is hesitant — she pulls back but does not leave. Sustained pursuit will overcome her.", ExampleScenarios = ["oscillating between push and pull", "staying when she should leave"] },
                new() { SortOrder = 6, MinValue = 26, MaxValue = 30, ResistanceLevel = "Ambivalent", Description = "Ambivalent — internally conflicted but not stopping.", PromptDirective = "Her loyalty is below-average. She is internally conflicted but does not stop the escalation. She may voice guilt but her actions do not align with her words. She needs only mild justification to continue.", ExampleScenarios = ["feeling guilty but not stopping", "needing only a small push"] },
                new() { SortOrder = 7, MinValue = 31, MaxValue = 35, ResistanceLevel = "Conditional", Description = "Conditional — needs a plausible justification before crossing a line.", PromptDirective = "She has moderate loyalty. She will not cross a significant line without a plausible justification — something that lets her rationalise the decision. Provide that justification in the narrative, and she will proceed.", ExampleScenarios = ["needing a reason to proceed", "rationalising her choice"] },
                new() { SortOrder = 8, MinValue = 36, MaxValue = 40, ResistanceLevel = "Reluctant Gate", Description = "Reluctant gatekeeper — wants to be persuaded.", PromptDirective = "Her loyalty is moderate. She acts as a reluctant gatekeeper — she wants to be persuaded and is looking for a reason. She will resist initially but is receptive to the right approach, emotional connection, or compelling circumstance.", ExampleScenarios = ["wanting to be convinced", "looking for permission to give in"] },
                new() { SortOrder = 9, MinValue = 41, MaxValue = 45, ResistanceLevel = "Selective Boundary", Description = "Selective boundaries — firm on some lines, porous on others.", PromptDirective = "Her loyalty is present but selective. She maintains boundaries on some acts but allows others. She compartmentalises — 'this is okay but that is not'. Respect the lines she draws while allowing escalation within her comfort zone.", ExampleScenarios = ["drawing partial lines", "compartmentalising intimacy"] },
                new() { SortOrder = 10, MinValue = 46, MaxValue = 50, ResistanceLevel = "Balanced Guard", Description = "Balanced guard — resists but can be moved by sustained, meaningful pursuit.", PromptDirective = "Her loyalty is around the midpoint. She maintains a balanced guard — she does not cross lines lightly, but sustained and meaningful pursuit (emotional connection, genuine desire, persistent but respectful approach) can move her boundaries over time.", ExampleScenarios = ["needing sustained pursuit", "emotional connection opening doors"] },
                new() { SortOrder = 11, MinValue = 51, MaxValue = 55, ResistanceLevel = "Moderate Firmness", Description = "Moderately firm — requires significant trust before lowering guard.", PromptDirective = "Her loyalty is above-average. She holds herself in check firmly. She requires significant trust, emotional safety, and a compelling situation before she will lower her guard. Do not have her fold under casual pressure.", ExampleScenarios = ["needing trust and safety", "significant emotional groundwork"] },
                new() { SortOrder = 12, MinValue = 56, MaxValue = 60, ResistanceLevel = "Guarded", Description = "Guarded — pulls back when escalation crosses her internal line.", PromptDirective = "Her loyalty is present and active. She is guarded and will pull back when escalation crosses her internal line. She may re-engage after pulling back if the other character respects her retreat. Do not have her stay past her comfort zone.", ExampleScenarios = ["pulling back at her line", "re-engaging after respect shown"] },
                new() { SortOrder = 13, MinValue = 61, MaxValue = 65, ResistanceLevel = "Firm Boundaries", Description = "Firm boundaries — says no and means it, requires genuine emotional engagement to reconsider.", PromptDirective = "She has real loyalty. She says no and means it. She will deflect, redirect, or physically create distance when boundaries are tested. She will only reconsider if genuine emotional engagement, vulnerability, or a significant situation changes her calculus.", ExampleScenarios = ["saying no and meaning it", "deflecting advances firmly"] },
                new() { SortOrder = 14, MinValue = 66, MaxValue = 70, ResistanceLevel = "Steadfast", Description = "Steadfast — holds the line consistently across encounters.", PromptDirective = "Her loyalty is strong. She holds the line consistently. She does not let a single encounter or moment of weakness redefine her boundaries. She may feel attraction but does not act on it. The other character must earn every inch of ground across multiple encounters.", ExampleScenarios = ["consistent boundaries across encounters", "not swayed by one moment"] },
                new() { SortOrder = 15, MinValue = 71, MaxValue = 75, ResistanceLevel = "Strong Resistance", Description = "Strong resistance — deflects and redirects; crossing requires deep emotional connection.", PromptDirective = "Her commitment is strong and active. She deflects and redirects when boundaries are tested. She will break off or redirect any interaction moving toward transgression and will not be talked back into it in the moment. Only deep emotional connection built over time, or a fundamental shift in her marriage, could change this.", ExampleScenarios = ["redirecting firmly", "breaking off risky moments"] },
                new() { SortOrder = 16, MinValue = 76, MaxValue = 80, ResistanceLevel = "Very Strong Resistance", Description = "Very strong resistance — only the most sustained and genuine pursuit has any chance.", PromptDirective = "Her commitment is very strong. She is alert to boundary threats and shuts them down cleanly. Only the most sustained, genuine, and emotionally deep pursuit — combined with her own internal crisis or marital breakdown — could potentially erode this over a very long arc.", ExampleScenarios = ["clean shutdowns", "alert to boundary threats"] },
                new() { SortOrder = 17, MinValue = 81, MaxValue = 85, ResistanceLevel = "Near-Immovable", Description = "Near-immovable — resistance is the default, compliance would require an arc-breaking event.", PromptDirective = "Her loyalty is near-absolute. Resistance is her default state and she enforces it calmly and consistently. Compliance with transgression would require an arc-breaking life event — the death of her marriage in her heart — not mere persistence or chemistry.", ExampleScenarios = ["calm, consistent enforcement", "only an arc-breaking event could change this"] },
                new() { SortOrder = 18, MinValue = 86, MaxValue = 90, ResistanceLevel = "Rigidly Faithful", Description = "Rigidly faithful — guilt at even the thought of crossing a line.", PromptDirective = "Her commitment is rigid. She feels genuine guilt at even the thought of crossing a line. She avoids situations where boundaries might be tested and removes herself if they arise. The affair narrative is not plausible for her without an extreme, transformative catalyst.", ExampleScenarios = ["avoiding risky situations", "guilt at thoughts of transgression"] },
                new() { SortOrder = 19, MinValue = 91, MaxValue = 95, ResistanceLevel = "Untouchable", Description = "Untouchable — the affair scenario is functionally off the table.", PromptDirective = "Her commitment is effectively absolute. The affair scenario is functionally off the table. She would end the interaction or leave the situation before any boundary is crossed. If the user wants an infidelity narrative, they must either lower her Loyalty significantly or select a more permissive ResistanceProfile.", ExampleScenarios = ["leaving the situation", "affair functionally impossible"] },
                new() { SortOrder = 20, MinValue = 96, MaxValue = 100, ResistanceLevel = "Unbreakable Vow", Description = "Unbreakable vow — transgression is unthinkable regardless of circumstances.", PromptDirective = "Her commitment is absolute and unshakeable. Transgression is unthinkable to her regardless of circumstances, chemistry, or marital deficit. She will not entertain it. If the user wants crossing to occur, this character's Loyalty must be lowered or a different ResistanceProfile must be selected.", ExampleScenarios = ["unshakeable fidelity", "transgression is unthinkable"] }
            ]
        };

        await _persistence.SaveStatResistanceProfileAsync(seeded, cancellationToken);
        _logger.LogInformation("Seeded default stat resistance profile.");
    }

    private static List<ResistanceThreshold> NormalizeThresholds(IReadOnlyList<ResistanceThreshold> thresholds)
    {
        return thresholds
            .Select((x, index) => new ResistanceThreshold
            {
                SortOrder = x.SortOrder <= 0 ? index + 1 : x.SortOrder,
                MinValue = Math.Clamp(x.MinValue, 0, 100),
                MaxValue = Math.Clamp(x.MaxValue, 0, 100),
                ResistanceLevel = (x.ResistanceLevel ?? string.Empty).Trim(),
                Description = (x.Description ?? string.Empty).Trim(),
                PromptDirective = (x.PromptDirective ?? string.Empty).Trim(),
                ExampleScenarios = x.ExampleScenarios
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.MinValue)
            .ToList();
    }

    private static void ValidateCoverage(IReadOnlyList<ResistanceThreshold> thresholds)
    {
        if (thresholds.Count == 0)
        {
            throw new ArgumentException("At least one threshold is required.");
        }

        foreach (var threshold in thresholds)
        {
            if (threshold.MinValue > threshold.MaxValue)
            {
                throw new ArgumentException($"Invalid threshold '{threshold.ResistanceLevel}': MinValue must be <= MaxValue.");
            }

            if (string.IsNullOrWhiteSpace(threshold.ResistanceLevel))
            {
                throw new ArgumentException("Each threshold requires a resistance level name.");
            }

            if (string.IsNullOrWhiteSpace(threshold.PromptDirective))
            {
                throw new ArgumentException($"Threshold '{threshold.ResistanceLevel}' requires a prompt directive.");
            }
        }

        var ordered = thresholds.OrderBy(x => x.MinValue).ThenBy(x => x.MaxValue).ToList();
        if (ordered[0].MinValue != 0 || ordered[^1].MaxValue != 100)
        {
            throw new ArgumentException("Threshold ranges must cover 0..100.");
        }

        var expectedMin = 0;
        foreach (var threshold in ordered)
        {
            if (threshold.MinValue != expectedMin)
            {
                throw new ArgumentException("Threshold ranges must be contiguous without gaps.");
            }

            expectedMin = threshold.MaxValue + 1;
        }

        if (expectedMin != 101)
        {
            throw new ArgumentException("Threshold ranges must end at 100.");
        }
    }
}
