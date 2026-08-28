using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

public sealed class IntensityProfileService : IIntensityProfileService
{
    private sealed record DefaultToneProfile(
        string Name,
        string Description,
        IntensityLevel Intensity,
        int BuildUpPhaseOffset = 0,
        int CommittedPhaseOffset = 0,
        int ApproachingPhaseOffset = 1,
        int ClimaxPhaseOffset = 2,
        int ResetPhaseOffset = -1,
        string ProseStyleDirective = "",
        string VoiceDirective = "",
        string ToneDirective = "",
        string FocusDirective = "",
        string HeatLevelDirective = "");

    private static readonly DefaultToneProfile[] PocDefaultProfiles =
    [
        new(IntensityLadder.GetLabel(IntensityLevel.Emotional), IntensityLadder.GetDefaultDescription(IntensityLevel.Emotional), IntensityLevel.Emotional,
            BuildUpPhaseOffset: 1, CommittedPhaseOffset: 2, ApproachingPhaseOffset: 3, ClimaxPhaseOffset: 4, ResetPhaseOffset: 0,
            ProseStyleDirective: "Intimate, tender prose. Connection revealed through small gestures and vulnerability.",
            VoiceDirective: "Favor emotional depth. Reveal the internal experience of intimacy.",
            ToneDirective: "Intimate and warm. Emotionally charged but restrained.",
            FocusDirective: "The deepening bond between characters. Trust, vulnerability, the risk of opening up.",
            HeatLevelDirective: "Emotional intimacy. Tender gestures, eye contact, hand-holding, closeness. Physical expressions are meaningful but limited — let emotional connection lead."),
        new(IntensityLadder.GetLabel(IntensityLevel.SuggestivePg12), IntensityLadder.GetDefaultDescription(IntensityLevel.SuggestivePg12), IntensityLevel.SuggestivePg12,
            ProseStyleDirective: "Playful, charged prose. Attraction conveyed through subtext and what goes unsaid.",
            VoiceDirective: "Express desire through suggestion and implication. Bodies speak where words do not.",
            ToneDirective: "Flirtatious and suggestive. Light but electric.",
            FocusDirective: "The thrill of the unspoken. Lingering glances, charged proximity, the anticipation of what might happen.",
            HeatLevelDirective: "Suggestive only. Flirtation, teasing dialogue, casual touch, brief kisses. Maintain erotic tension through subtext — no explicit physical content."),
        new(IntensityLadder.GetLabel(IntensityLevel.SensualMature), IntensityLadder.GetDefaultDescription(IntensityLevel.SensualMature), IntensityLevel.SensualMature,
            ProseStyleDirective: "Sensory-driven prose. The body's experience of desire — what is touched, tasted, felt.",
            VoiceDirective: "Build anticipation through physical detail. Describe each escalation as it happens.",
            ToneDirective: "Warm and intimate. The heat of a touch, the weight of a look.",
            FocusDirective: "The progression of physical intimacy. From tension to touch to the edge of surrender.",
            HeatLevelDirective: "Sensual intimacy. Passionate kissing, caressing, exploration. Describe physical sensation and responsiveness. Avoid graphic anatomical detail."),
        new(IntensityLadder.GetLabel(IntensityLevel.Explicit), IntensityLadder.GetDefaultDescription(IntensityLevel.Explicit), IntensityLevel.Explicit,
            ProseStyleDirective: "Direct, visceral prose. Bodies and pleasure described without euphemism.",
            VoiceDirective: "Stay grounded in physical reality. Describe each act as it unfolds.",
            ToneDirective: "Raw and intimate. The physical truth of the moment.",
            FocusDirective: "Physical pleasure. Arousal, penetration, response, climax — what the body experiences.",
            HeatLevelDirective: "Explicit intimacy. Clear descriptions of sexual acts, arousal, penetration, and orgasm. Direct language. Graphic when the scene demands it."),
        new(IntensityLadder.GetLabel(IntensityLevel.Hardcore), IntensityLadder.GetDefaultDescription(IntensityLevel.Hardcore), IntensityLevel.Hardcore,
            ProseStyleDirective: "Unflinching, explicit prose. Anatomical detail, raw language, no softening.",
            VoiceDirective: "Prioritize physical description and sensation. Bodies in motion, pleasure unfiltered.",
            ToneDirective: "Raw, intense, unapologetically explicit.",
            FocusDirective: "Maximum physical detail. Every act, every reaction, every sensation — breasts, genitals, penetration, oral sex, orgasms, cum shots.",
            HeatLevelDirective: "Hardcore. Graphic descriptions of all sexual acts and anatomy. Raw language throughout. Emphasis on visual detail, physical sensation, and female pleasure. No romantic filters — pure, intense erotica.")
    ];

    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<IntensityProfileService> _logger;

    public IntensityProfileService(ISqlitePersistence persistence, ILogger<IntensityProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<IntensityProfile> CreateAsync(
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        string proseStyleDirective = "",
        string voiceDirective = "",
        string toneDirective = "",
        string focusDirective = "",
        string heatLevelDirective = "",
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existingProfiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        var characterProfileCount = existingProfiles.Count(x => x.Intensity != IntensityLevel.Intro);
        if (characterProfileCount >= PocDefaultProfiles.Length)
        {
            throw new InvalidOperationException($"POC is limited to {PocDefaultProfiles.Length} tone profiles.");
        }

        if (intensity == IntensityLevel.Intro)
        {
            throw new InvalidOperationException("Atmospheric is narrative-only and cannot be used as a character intensity profile.");
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tone profile name cannot be empty.", nameof(name));
        }

        if (existingProfiles.Any(x => string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Tone profile name already exists.");
        }

        if (existingProfiles.Any(x => x.Intensity == intensity))
        {
            throw new InvalidOperationException($"An intensity profile already exists for level '{intensity}'. Exactly one profile per intensity level is supported.");
        }

        var profile = new IntensityProfile
        {
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            Intensity = intensity,
            BuildUpPhaseOffset = buildUpPhaseOffset,
            CommittedPhaseOffset = committedPhaseOffset,
            ApproachingPhaseOffset = approachingPhaseOffset,
            ClimaxPhaseOffset = climaxPhaseOffset,
            ResetPhaseOffset = resetPhaseOffset,
            ProseStyleDirective = proseStyleDirective?.Trim() ?? string.Empty,
            VoiceDirective = voiceDirective?.Trim() ?? string.Empty,
            ToneDirective = toneDirective?.Trim() ?? string.Empty,
            FocusDirective = focusDirective?.Trim() ?? string.Empty,
            HeatLevelDirective = heatLevelDirective?.Trim() ?? string.Empty,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };

        await _persistence.SaveToneProfileAsync(profile, cancellationToken);
        _logger.LogInformation("Tone profile created: {ToneProfileId}, Name={Name}", profile.Id, profile.Name);
        return profile;
    }

    public Task<List<IntensityProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        return ListInternalAsync(cancellationToken);
    }

    public Task<IntensityProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        return _persistence.LoadToneProfileAsync(id, cancellationToken);
    }

    public async Task<IntensityProfile?> UpdateAsync(
        string id,
        string name,
        string description,
        IntensityLevel intensity,
        int buildUpPhaseOffset,
        int committedPhaseOffset,
        int approachingPhaseOffset,
        int climaxPhaseOffset,
        int resetPhaseOffset,
        string proseStyleDirective = "",
        string voiceDirective = "",
        string toneDirective = "",
        string focusDirective = "",
        string heatLevelDirective = "",
        CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var existing = await _persistence.LoadToneProfileAsync(id, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new ArgumentException("Tone profile name cannot be empty.", nameof(name));
        }

        var profiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        if (profiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Tone profile name already exists.");
        }

        if (profiles.Any(x => !string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
            && x.Intensity == intensity))
        {
            throw new InvalidOperationException($"An intensity profile already exists for level '{intensity}'. Exactly one profile per intensity level is supported.");
        }

        if (intensity == IntensityLevel.Intro)
        {
            throw new InvalidOperationException("Atmospheric is narrative-only and cannot be used as a character intensity profile.");
        }

        existing.Name = trimmedName;
        existing.Description = description?.Trim() ?? string.Empty;
        existing.Intensity = intensity;
        existing.BuildUpPhaseOffset = buildUpPhaseOffset;
        existing.CommittedPhaseOffset = committedPhaseOffset;
        existing.ApproachingPhaseOffset = approachingPhaseOffset;
        existing.ClimaxPhaseOffset = climaxPhaseOffset;
        existing.ResetPhaseOffset = resetPhaseOffset;
        existing.ProseStyleDirective = proseStyleDirective?.Trim() ?? string.Empty;
        existing.VoiceDirective = voiceDirective?.Trim() ?? string.Empty;
        existing.ToneDirective = toneDirective?.Trim() ?? string.Empty;
        existing.FocusDirective = focusDirective?.Trim() ?? string.Empty;
        existing.HeatLevelDirective = heatLevelDirective?.Trim() ?? string.Empty;
        existing.UpdatedUtc = DateTime.UtcNow;

        await _persistence.SaveToneProfileAsync(existing, cancellationToken);
        _logger.LogInformation("Tone profile updated: {ToneProfileId}, Name={Name}", existing.Id, existing.Name);
        return existing;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);

        var deleted = await _persistence.DeleteToneProfileAsync(id, cancellationToken);
        _logger.LogInformation("Tone profile deleted: {ToneProfileId}, Success={Deleted}", id, deleted);
        return deleted;
    }

    private async Task<List<IntensityProfile>> ListInternalAsync(CancellationToken cancellationToken)
    {
        await EnsureDefaultProfilesAsync(cancellationToken);
        return await _persistence.LoadAllToneProfilesAsync(cancellationToken);
    }

    private async Task EnsureDefaultProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = await _persistence.LoadAllToneProfilesAsync(cancellationToken);
        var changed = false;

        foreach (var item in PocDefaultProfiles)
        {
            var existing = profiles.FirstOrDefault(x => x.Intensity == item.Intensity);
            if (existing is null)
            {
                var profile = new IntensityProfile
                {
                    Name = item.Name,
                    Description = item.Description,
                    Intensity = item.Intensity,
                    BuildUpPhaseOffset = item.BuildUpPhaseOffset,
                    CommittedPhaseOffset = item.CommittedPhaseOffset,
                    ApproachingPhaseOffset = item.ApproachingPhaseOffset,
                    ClimaxPhaseOffset = item.ClimaxPhaseOffset,
                    ResetPhaseOffset = item.ResetPhaseOffset,
                    ProseStyleDirective = item.ProseStyleDirective,
                    VoiceDirective = item.VoiceDirective,
                    ToneDirective = item.ToneDirective,
                    FocusDirective = item.FocusDirective,
                    HeatLevelDirective = item.HeatLevelDirective,
                    CreatedUtc = DateTime.UtcNow,
                    UpdatedUtc = DateTime.UtcNow
                };

                await _persistence.SaveToneProfileAsync(profile, cancellationToken);
                changed = true;
                continue;
            }

            var shouldBackfillName = string.IsNullOrWhiteSpace(existing.Name);
            var shouldBackfillDescription = string.IsNullOrWhiteSpace(existing.Description);
            var shouldBackfillProse = string.IsNullOrWhiteSpace(existing.ProseStyleDirective);
            var shouldBackfillVoice = string.IsNullOrWhiteSpace(existing.VoiceDirective);
            var shouldBackfillTone = string.IsNullOrWhiteSpace(existing.ToneDirective);
            var shouldBackfillFocus = string.IsNullOrWhiteSpace(existing.FocusDirective);
            var shouldBackfillHeat = string.IsNullOrWhiteSpace(existing.HeatLevelDirective);
            if (shouldBackfillName || shouldBackfillDescription || shouldBackfillProse || shouldBackfillVoice || shouldBackfillTone || shouldBackfillFocus || shouldBackfillHeat)
            {
                if (shouldBackfillName)
                {
                    existing.Name = item.Name;
                }

                if (shouldBackfillDescription)
                {
                    existing.Description = item.Description;
                }

                if (shouldBackfillProse)
                {
                    existing.ProseStyleDirective = item.ProseStyleDirective;
                }

                if (shouldBackfillVoice)
                {
                    existing.VoiceDirective = item.VoiceDirective;
                }

                if (shouldBackfillTone)
                {
                    existing.ToneDirective = item.ToneDirective;
                }

                if (shouldBackfillFocus)
                {
                    existing.FocusDirective = item.FocusDirective;
                }

                if (shouldBackfillHeat)
                {
                    existing.HeatLevelDirective = item.HeatLevelDirective;
                }

                existing.UpdatedUtc = DateTime.UtcNow;
                await _persistence.SaveToneProfileAsync(existing, cancellationToken);
                changed = true;
            }
        }

        if (changed)
        {
            _logger.LogInformation("Ensured {Count} canonical tone profiles for adaptive POC.", PocDefaultProfiles.Length);
        }
    }
}