using DreamGenClone.Application.StoryAnalysis;
using DreamGenClone.Application.StoryAnalysis.Abstractions;
using DreamGenClone.Domain.StoryAnalysis;
using DreamGenClone.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace DreamGenClone.Infrastructure.StoryAnalysis;

/// <summary>
/// Implements <see cref="ICharacterProfileService"/> using SQLite persistence. B-042 T021.
/// </summary>
public sealed class CharacterProfileService : ICharacterProfileService
{
    private readonly ISqlitePersistence _persistence;
    private readonly ILogger<CharacterProfileService> _logger;

    public CharacterProfileService(
        ISqlitePersistence persistence,
        ILogger<CharacterProfileService> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    public async Task<CharacterProfile?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        var profile = await _persistence.LoadCharacterProfileAsync(id, cancellationToken);
        if (profile is null)
        {
            _logger.LogWarning("CharacterProfile {Id} not found", id);
        }
        return profile;
    }

    public async Task<IReadOnlyList<CharacterProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var all = await _persistence.LoadAllCharacterProfilesAsync(cancellationToken);
        return all
            .OrderBy(p => p.TargetRole, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<CharacterProfile>> GetByRoleAsync(string targetRole, CancellationToken cancellationToken = default)
    {
        var all = await _persistence.LoadAllCharacterProfilesAsync(cancellationToken);
        return all
            .Where(p =>
                string.Equals(p.TargetRole, targetRole, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.TargetRole, "Any", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveAsync(CharacterProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Strip any non-canonical keys (e.g. legacy Tension/Connection) before validation.
        profile.CharacterStats = AdaptiveStatCatalog.NormalizeComplete(profile.CharacterStats);

        ValidateStats(profile);

        profile.UpdatedUtc = DateTime.UtcNow;
        if (profile.CreatedUtc == default)
        {
            profile.CreatedUtc = DateTime.UtcNow;
        }

        await _persistence.SaveCharacterProfileAsync(profile, cancellationToken);
        _logger.LogInformation("CharacterProfile {Id} ({Name}) saved", profile.Id, profile.Name);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var existing = await _persistence.LoadCharacterProfileAsync(id, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        if (existing.IsSeeded)
        {
            _logger.LogWarning("CharacterProfile {Id} is a seeded default and cannot be deleted", id);
            return false;
        }

        var deleted = await _persistence.DeleteCharacterProfileAsync(id, cancellationToken);
        if (deleted)
        {
            _logger.LogInformation("CharacterProfile {Id} deleted", id);
        }
        return deleted;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var profile in SeedArchetypes)
        {
            await _persistence.SaveCharacterProfileAsync(profile, cancellationToken);
        }
    }

    private static void ValidateStats(CharacterProfile profile)
    {
        var validStatNames = new HashSet<string>(
            AdaptiveStatCatalog.CanonicalStatNames,
            StringComparer.OrdinalIgnoreCase);

        foreach (var key in profile.CharacterStats.Keys)
        {
            if (!validStatNames.Contains(key))
            {
                throw new ArgumentException(
                    $"CharacterStats key '{key}' is not a canonical stat name. Valid names: {string.Join(", ", AdaptiveStatCatalog.CanonicalStatNames)}",
                    nameof(profile));
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.TargetRole) &&
            !string.Equals(profile.TargetRole, "Any", StringComparison.OrdinalIgnoreCase))
        {
            var validDimNames = new HashSet<string>(
                BehavioralDimensionCatalog.GetDimensions(profile.TargetRole).Select(d => d.Name),
                StringComparer.OrdinalIgnoreCase);

            if (validDimNames.Count > 0)
            {
                foreach (var key in profile.EncounterStats.Keys)
                {
                    if (!validDimNames.Contains(key))
                    {
                        throw new ArgumentException(
                            $"EncounterStats key '{key}' is not a valid dimension for role '{profile.TargetRole}'. Valid keys: {string.Join(", ", validDimNames)}",
                            nameof(profile));
                    }
                }
            }
        }
    }

    // ── Seeded archetypes ────────────────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<CharacterProfile> SeedArchetypes = BuildSeedArchetypes();

    private static List<CharacterProfile> BuildSeedArchetypes()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        CharacterProfile H(string id, string name, string description, string additionalNotes,
            int desire, int restraint, int tension, int connection, int dominance, int loyalty, int selfRespect,
            int awareness, int acceptance, int voyeurism, int participation, int encouragement, int riskTolerance,
            int attentiveness, int intimacyAvailability)
            => new()
            {
                Id = id,
                Name = name,
                Description = description,
                AdditionalNotes = additionalNotes,
                TargetRole = "Husband",
                TargetGender = "Male",
                IsSeeded = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                CharacterStats = new Dictionary<string, int>
                {
                    ["Desire"] = desire, ["Restraint"] = restraint,
                    ["Dominance"] = dominance, ["Loyalty"] = loyalty,
                    ["SelfRespect"] = selfRespect
                },
                EncounterStats = new Dictionary<string, int>
                {
                    ["Awareness"] = awareness, ["Acceptance"] = acceptance, ["Voyeurism"] = voyeurism,
                    ["Participation"] = participation, ["Encouragement"] = encouragement,
                    ["RiskTolerance"] = riskTolerance,
                    ["Attentiveness"] = attentiveness, ["IntimacyAvailability"] = intimacyAvailability
                }
            };

        CharacterProfile W(string id, string name, string description, string additionalNotes,
            int desire, int restraint, int tension, int connection, int dominance, int loyalty, int selfRespect,
            int discoveryCaution, int exhibitionism, int emotionalEngagement, int postEncounterGuilt,
            int boundaryFirmness, int seductionReceptivity)
            => new()
            {
                Id = id,
                Name = name,
                Description = description,
                AdditionalNotes = additionalNotes,
                TargetRole = "Wife",
                TargetGender = "Female",
                IsSeeded = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                CharacterStats = new Dictionary<string, int>
                {
                    ["Desire"] = desire, ["Restraint"] = restraint,
                    ["Dominance"] = dominance, ["Loyalty"] = loyalty,
                    ["SelfRespect"] = selfRespect
                },
                EncounterStats = new Dictionary<string, int>
                {
                    ["DiscoveryCaution"] = discoveryCaution, ["Exhibitionism"] = exhibitionism,
                    ["EmotionalEngagement"] = emotionalEngagement, ["PostEncounterGuilt"] = postEncounterGuilt,
                    ["BoundaryFirmness"] = boundaryFirmness, ["SeductionReceptivity"] = seductionReceptivity
                }
            };

        CharacterProfile O(string id, string name, string description, string additionalNotes,
            int desire, int restraint, int tension, int connection, int dominance, int loyalty, int selfRespect,
            int husbandAwareness, int marriageContextUse, int discoveryRisk, int persistencePastLimits)
            => new()
            {
                Id = id,
                Name = name,
                Description = description,
                AdditionalNotes = additionalNotes,
                TargetRole = "OtherMan",
                TargetGender = "Male",
                IsSeeded = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                CharacterStats = new Dictionary<string, int>
                {
                    ["Desire"] = desire, ["Restraint"] = restraint,
                    ["Dominance"] = dominance, ["Loyalty"] = loyalty,
                    ["SelfRespect"] = selfRespect
                },
                EncounterStats = new Dictionary<string, int>
                {
                    ["HusbandAwareness"] = husbandAwareness, ["MarriageContextUse"] = marriageContextUse,
                    ["DiscoveryRisk"] = discoveryRisk, ["PersistencePastLimits"] = persistencePastLimits
                }
            };

        return
        [
            // ── Husband ──────────────────────────────────────────────────────────────────────
            H("seed-h-oblivious",    "Oblivious / Inattentive Husband",
                "Emotionally checked out, barely notices his wife's needs or the world around him.",
                "Emotionally checked out and barely registers what is happening around him.",
                35, 65, 20, 25, 55, 50, 60,  10, 15,  5,   0,  5, 10,  10, 15),
            H("seed-h-suspicious",   "Suspicious Husband",
                "Alert and watchful, he senses something is off but cannot prove it yet.",
                "He is growing suspicious and watches for signs of betrayal.",
                30, 55, 80, 40, 55, 60, 50,  45, 20, 25,   0,  5, 20,  25, 30),
            H("seed-h-caring",       "Caring / Supportive Husband",
                "Attentive and emotionally present, he genuinely wants his wife to be happy.",
                "He is attentive and deeply invested in his wife's happiness and well-being.",
                50, 60, 25, 90, 45, 95, 80,  50, 65, 30,  20, 55, 35,  85, 75),
            H("seed-h-cuckold",      "Cuckold Husband",
                "Derives arousal from his wife's encounters; his awareness is part of the fantasy.",
                "He is aroused by the idea of his wife being with another man.",
                85, 40, 50, 60, 20, 80, 40,  85, 70, 80,  20, 45, 40,  55, 20),
            H("seed-h-fantasy",      "Fantasy-Driven / Hotwife Husband",
                "Lives for the fantasy of his wife being desired; he fuels the fire from the sidelines.",
                "He actively fuels the fantasy of sharing his wife and watching her be desired.",
                80, 35, 40, 65, 55, 65, 50,  95, 90, 85,  70, 80, 65,  60, 50),
            H("seed-h-swinger",      "Swinger — Full Participant",
                "Fully engaged in an open lifestyle; everyone knows and everyone participates.",
                "He is an enthusiastic participant in a fully open and consensual lifestyle.",
                90, 25, 20, 70, 60, 75, 85, 100,100, 10, 100, 90, 75,  75, 85),
            H("seed-h-controlling",  "Controlling Husband",
                "Needs to control every aspect of the relationship; connection is secondary to authority.",
                "He exerts tight control over the relationship and his wife's choices.",
                45, 50, 40, 50, 90, 70, 70,  60, 25, 20,  15, 10, 20,  30, 25),
            H("seed-h-shocked",      "Shocked / Confused Husband",
                "Stunned by what he has discovered; he does not know how to process or react.",
                "He is stunned and confused, struggling to process what he has discovered.",
                55, 65, 85, 35, 30, 55, 40,  70, 15, 40,   5,  5, 15,  50, 35),

            // ── Wife ─────────────────────────────────────────────────────────────────────────
            W("seed-w-loyal",        "Loyal Good Wife",
                "Devoted to her marriage; the thought of betrayal genuinely distresses her.",
                "She is devoted to her marriage and the thought of betrayal deeply distresses her.",
                40, 85, 30, 90, 45, 95, 80,  90,  5, 75,  95,  85, 10),
            W("seed-w-prude",        "Prude Wife",
                "Deeply inhibited by propriety and guilt; she would resist even the suggestion of impropriety.",
                "She is deeply inhibited and resists even the suggestion of impropriety.",
                15, 95, 45, 85, 60, 95, 85,  95,  0, 60, 100,  95,  5),
            W("seed-w-shy",          "Shy / Reserved Wife",
                "Quiet and cautious; she needs trust and safety before she opens up.",
                "She is quiet and guarded, needing deep trust before she ever opens up.",
                35, 90, 70, 75, 30, 85, 65,  85,  5, 70,  85,  80, 10),
            W("seed-w-curious",      "Curious / Exploring Wife",
                "Wondering what she might be missing; her curiosity is a crack in the door.",
                "She is curious about what she might be missing and wonders about other possibilities.",
                50, 55, 50, 65, 40, 65, 60,  65, 20, 60,  60,  55, 40),
            W("seed-w-cheating",     "Cheating Wife",
                "Already crossed the line; she is receptive to pursuit and her boundaries are down.",
                "She has already crossed the line and is receptive to further pursuit.",
                60, 70, 65, 30, 50, 25, 55,  75, 35, 45,  40,  25, 65),
            W("seed-w-neglected",    "Neglected Wife",
                "Starved for attention and intimacy; someone who notices her can get through.",
                "She is starved for attention and vulnerable to anyone who truly notices her.",
                80, 60, 55, 25, 40, 50, 45,  50, 45, 80,  50,  30, 70),
            W("seed-w-empowered",    "Empowered / Confident Wife",
                "Knows what she wants and is not afraid to set terms; her choices are deliberate.",
                "She knows what she wants and makes deliberate, self-assured choices on her own terms.",
                65, 40, 30, 70, 70, 70, 90,  40, 60, 40,  20,  75, 20),
            W("seed-w-hotwife",      "Slut Wife / Hotwife",
                "Embraces her sexuality openly; boundaries exist only to be playfully tested.",
                "She embraces her sexuality openly and treats boundaries as optional.",
                70, 20, 35, 70, 65, 80, 75,  15, 85, 25,   5,  10, 80),
            W("seed-w-nympho",       "Nymphomaniac Wife",
                "Driven by insatiable desire; restraint and boundaries are foreign concepts.",
                "She is driven by insatiable desire and recognizes no restraints or boundaries.",
                85,  5, 20, 30, 40, 15, 25,   5, 95, 10,   0,   5, 95),

            // ── OtherMan ─────────────────────────────────────────────────────────────────────
            O("seed-o-niceguy",      "The Nice Guy",
                "Kind, patient, and respectful — he would never push, only wait and hope.",
                "He is patient, kind, and would never push — he waits and hopes to be noticed.",
                50, 75, 40, 80, 30, 75, 60,  75, 10, 80,  10),
            O("seed-o-nerd",         "The Nerd",
                "Awkward but genuine; underestimated by everyone, including himself.",
                "He is awkward but genuine, and is underestimated by everyone including himself.",
                70, 80, 60, 50, 20, 85, 40,  70, 15, 75,  15),
            O("seed-o-youngegger",   "The Young Eager Guy",
                "Enthusiastic, hungry, and impulsive — he is driven by desire more than strategy.",
                "He is young, eager, and impulsive — driven by raw desire more than any plan.",
                85, 40, 50, 60, 45, 40, 55,  30, 20, 35,  55),
            O("seed-o-charmer",      "The Charmer",
                "Smooth, magnetic, and intuitive — he knows exactly what she needs to hear.",
                "He is smooth and intuitive, always knowing exactly what she needs to hear.",
                60, 50, 20, 75, 60, 30, 85,  60, 50, 50,  45),
            O("seed-o-experienced",  "The Experienced Older Man",
                "Worldly, composed, and unhurried — he has done this before and knows how to handle it.",
                "He is worldly and composed, with the confidence of someone who has done this before.",
                75, 70, 25, 50, 75, 35, 85,  80, 40, 65,  35),
            O("seed-o-jedi",         "The Jedi Master",
                "A master of psychological seduction; he dismantles boundaries with surgical precision.",
                "He dismantles emotional boundaries with psychological precision and control.",
                70, 50, 20, 85, 90, 15, 10,  90, 85, 40,  80),
            O("seed-o-cocky",        "The Confident Cocky Guy",
                "Bold, brash, and unapologetic — he takes what he wants and dares her to stop him.",
                "He is bold and unapologetic, taking what he wants and daring her to stop him.",
                75, 35, 15, 40, 80, 25, 95,  25, 30, 25,  70),
            O("seed-o-bull",         "The Bull",
                "Pure dominant energy; he is here for one thing and makes no apologies for it.",
                "He exudes pure dominant energy and makes no apologies for what he wants.",
                90, 20, 25, 30, 95, 20, 90,  10, 15,  5,  90),
        ];
    }
}
